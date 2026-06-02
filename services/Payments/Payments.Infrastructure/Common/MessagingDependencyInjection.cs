using Confluent.Kafka;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Common.Messaging;
using Payments.Infrastructure.Messaging.Kafka.Config;
using Payments.Infrastructure.Messaging.Kafka.PaymentCommands;
using Payments.Infrastructure.Persistence.Database;
using Platform.KafkaFlow.DeadLetter;
using Platform.KafkaFlow.DeadLetter.Common;
using Platform.KafkaFlow.Inbox.EFCore.Common;
using Platform.KafkaFlow.ProducerHeaders;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using AvroAuthorizePaymentCommand = Payments.Transactions.AuthorizePaymentCommand;
using AvroCapturePaymentCommand = Payments.Transactions.CapturePaymentCommand;
using AvroRequestRefundCommand = Payments.Transactions.RequestRefundCommand;
using AvroVoidPaymentCommand = Payments.Transactions.VoidPaymentCommand;

namespace Payments.Infrastructure.Common;

/// <summary>
/// DI wiring for Kafka (saga-command consumer + outbox serialisation) and for the inbox dedup
/// adapter against <see cref="PaymentsDbContext"/>. Payments has no producers in v1 — publish
/// path is 100% through the transactional outbox + <c>outbox-relay-payments</c> container
/// (ADR-0001).
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// Service origin identifier written to the <c>origin</c> Kafka header by the
    /// producer-headers middleware and the outbox relay.
    /// </summary>
    internal const string KafkaProducerOrigin = "Payments";

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

        services.AddOptionsWithValidateOnStart<PaymentCommandsConsumerOptions>()
            .BindConfiguration(PaymentCommandsConsumerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var consumerOptions = configuration
            .GetRequiredSection(PaymentCommandsConsumerOptions.Section)
            .Get<PaymentCommandsConsumerOptions>()!;
        consumerOptions.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;

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
                    .Topic(topicsOptions.PaymentCommands)
                    .WithConsumerConfig(consumerOptions)
                    .WithBufferSize(consumerOptions.BufferSize)
                    .WithWorkersCount(consumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        // Middleware order -> outermost to innermost.
                        .AddCorrelationIdConsumerMiddleware()
                        .AddDeadLetter()
                        // ADR-0025: one classified RetryForever governs all consumers — no
                        // money-handling exception. Retry iff the failure is transient/retryable
                        // (ConsumerRetry.IsRetryable); a poison command (e.g. a 23505 unique-violation)
                        // is not handled here and falls through to AddDeadLetter, routing to
                        // payments.payment-commands.Payments.DLT so the partition keeps advancing.
                        // Transient faults retry forever with the consumer paused — never
                        // dead-lettered. DLT operations: docs/bc-design/kafka-dlt-strategy.md.
                        .RetryForever(config => config
                            .Handle(ctx => ConsumerRetry.IsRetryable(ctx.Exception))
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)))
                        .AddInbox(
                            typeof(AvroAuthorizePaymentCommand),
                            typeof(AvroCapturePaymentCommand),
                            typeof(AvroVoidPaymentCommand),
                            typeof(AvroRequestRefundCommand))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<AuthorizePaymentCommandKafkaHandler>()
                            .AddHandler<CapturePaymentCommandKafkaHandler>()
                            .AddHandler<VoidPaymentCommandKafkaHandler>()
                            .AddHandler<RequestRefundCommandKafkaHandler>())
                    )
                ))
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddInbox<PaymentsDbContext>();
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
