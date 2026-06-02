using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Common.Messaging;
using Ordering.Infrastructure.Messaging.Kafka.Config;
using Ordering.Infrastructure.Messaging.Kafka.SagaCommands;
using Ordering.Infrastructure.Persistence.Database;
using Platform.KafkaFlow.DeadLetter;
using Platform.KafkaFlow.DeadLetter.Common;
using Platform.KafkaFlow.Inbox.EFCore.Common;
using Platform.KafkaFlow.ProducerHeaders;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using AvroCancelOrderCommand = Ordering.Orders.CancelOrderCommand;
using AvroConfirmOrderCommand = Ordering.Orders.ConfirmOrderCommand;
using AvroCreateOrderCommand = Ordering.Orders.CreateOrderCommand;
using AvroMarkOrderFailedCommand = Ordering.Orders.MarkOrderFailedCommand;

namespace Ordering.Infrastructure.Common;

/// <summary>
/// DI wiring for Kafka (saga-command consumer + outbox serialisation) and
/// for the inbox dedup adapter against <c>OrderingDbContext</c>. Ordering
/// has no producers in v1 — publish path is 100% through the transactional
/// outbox + <c>outbox-relay-ordering</c> container (ADR-0001).
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// Service origin identifier written to the <c>origin</c> Kafka header
    /// by the producer-headers middleware and the outbox relay.
    /// </summary>
    internal const string KafkaProducerOrigin = "Ordering";

    internal static IServiceCollection AddKafkaMessaging(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<KafkaOptions>()
            .BindConfiguration(KafkaOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<TopicsOptions>()
            .BindConfiguration(TopicsOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<SchemaRegistryOptions>()
            .BindConfiguration(SchemaRegistryOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<AvroSerializerOptions>()
            .BindConfiguration(AvroSerializerOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<OrderCommandsConsumerOptions>()
            .BindConfiguration(OrderCommandsConsumerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var consumerOptions = configuration
            .GetRequiredSection(OrderCommandsConsumerOptions.Section)
            .Get<OrderCommandsConsumerOptions>()!;

        var topicsOptions = configuration
            .GetRequiredSection(TopicsOptions.Section)
            .Get<TopicsOptions>()!;

        services.AddKafka(kafka => kafka
            .AddCluster(cluster => cluster
                .WithBrokers(kafkaOptions.Brokers)
                .WithSchemaRegistry(config => config.Url = kafkaOptions.SchemaRegistry.Url)
                .AddDltProducer(
                    topicsOptions.DltTopicSuffix,
                    producer => producer
                        .AddMiddlewares(m => m
                            .AddProducerHeaders(KafkaProducerOrigin)
                            .AddSchemaRegistryAvroSerializer(kafkaOptions.AvroSerializer)))
                .AddConsumer(consumer => consumer
                    .Topic(topicsOptions.OrderCommands)
                    .WithConsumerConfig(consumerOptions.WithCooperativeRebalancing())
                    .WithBufferSize(consumerOptions.BufferSize)
                    .WithWorkersCount(consumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        // Middleware order -> outermost to innermost.
                        .AddCorrelationIdConsumerMiddleware()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle(ctx => ConsumerRetry.IsRetryable(ctx.Exception))
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)))
                        .AddInbox(
                            typeof(AvroCreateOrderCommand),
                            typeof(AvroConfirmOrderCommand),
                            typeof(AvroCancelOrderCommand),
                            typeof(AvroMarkOrderFailedCommand))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<CreateOrderCommandKafkaHandler>()
                            .AddHandler<ConfirmOrderCommandKafkaHandler>()
                            .AddHandler<CancelOrderCommandKafkaHandler>()
                            .AddHandler<MarkOrderFailedCommandKafkaHandler>())
                    )
                ))
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddInbox<OrderingDbContext>();
        services.AddOutbox(outbox =>
        {
            outbox.ConfigureMessageOrigin(KafkaProducerOrigin);

            outbox.ConfigureAvroSerializerConfig(options =>
            {
                configuration.Bind(AvroSerializerOptions.Section, options);
            });

            outbox.ConfigureSchemaRegistryConfig(options =>
            {
                configuration.Bind(SchemaRegistryOptions.Section, options);
            });
        });

        return services;
    }
}
