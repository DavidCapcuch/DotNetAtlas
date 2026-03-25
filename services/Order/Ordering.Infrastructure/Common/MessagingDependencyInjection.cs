using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Order.AlertSubscriptions;
using Ordering.Application.Common.Messaging;
using Ordering.Application.Common.Observability;
using Ordering.Infrastructure.Common.Config.Kafka;
using Ordering.Infrastructure.Common.Persistence.Database;
using Ordering.Infrastructure.Messaging.Kafka.Handlers;
using Platform.KafkaFlow.DeadLetter.Common;
using Platform.KafkaFlow.Inbox.EFCore.Common;
using Platform.KafkaFlow.ProducerHeaders;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Ordering.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for communication infrastructure.
/// The Ordering service publishes order initiation events via the transactional outbox
/// and consumes saga outcome events to update order status.
/// </summary>
internal static class MessagingDependencyInjection
{
    private const string KafkaProducerOrigin = ApplicationInfo.AppName;

    /// <summary>
    /// Configures Kafka messaging with outbox publishing and saga outcome consumers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddKafkaMessaging(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<TopicsOptions>()
            .BindConfiguration(TopicsOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<KafkaConsumerOptions>()
            .BindConfiguration(KafkaConsumerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var consumerOptions = configuration
            .GetRequiredSection(KafkaConsumerOptions.Section)
            .Get<KafkaConsumerOptions>()!;

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
                    .Topic(topicsOptions.OrderAlertSubscriptions)
                    .WithConsumerConfig(consumerOptions)
                    .WithBufferSize(consumerOptions.BufferSize)
                    .WithWorkersCount(consumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle<DbUpdateException>()
                            .Handle<SqlException>()
                            .Handle<TimeoutException>()
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
                                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
                                TimeSpan.FromSeconds(60)))
                        .AddInbox(
                            typeof(AlertSubscriptionPurchaseCompletedEvent),
                            typeof(AlertSubscriptionPurchaseFailedEvent),
                            typeof(AlertSubscriptionExtensionCompletedEvent),
                            typeof(AlertSubscriptionExtensionFailedEvent))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<PurchaseCompletedKafkaHandler>()
                            .AddHandler<PurchaseFailedKafkaHandler>()
                            .AddHandler<ExtensionCompletedKafkaHandler>()
                            .AddHandler<ExtensionFailedKafkaHandler>())
                    )
            ))
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddInbox<OrderingDbContext>();
        services.AddOutbox(outbox =>
        {
            outbox.ConfigureMessageOrigin(ApplicationInfo.AppName);

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
