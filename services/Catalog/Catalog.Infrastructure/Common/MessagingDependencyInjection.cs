using Catalog.Application.Common.Messaging;
using Catalog.Infrastructure.Messaging.Kafka.Config;
using Catalog.Infrastructure.Messaging.Kafka.StockEvents;
using Catalog.Infrastructure.Persistence.Database;
using Confluent.Kafka;
using Inventory.Stock;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Platform.KafkaFlow.DeadLetter;
using Platform.KafkaFlow.DeadLetter.Common;
using Platform.KafkaFlow.Inbox.EFCore.Common;
using Platform.KafkaFlow.ProducerHeaders;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Catalog.Infrastructure.Common;

/// <summary>
/// DI wiring for Kafka — outbox serialization (Catalog publishes via the transactional
/// outbox + <c>outbox-relay-catalog</c> per ADR-0001, no Kafka producers in v1) and the
/// inbound <c>TopicsOptions.InventoryStockEvents</c> consumer.
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// Service origin identifier written to the <c>origin</c> Kafka header by the
    /// producer-headers middleware (DLT producer in-process) AND by the outbox relay
    /// (separate <c>outbox-relay-catalog</c> container). The two MUST agree — the relay
    /// container reads its origin from <c>OUTBOX_MESSAGE_ORIGIN</c>; downstream consumers
    /// attribute events by this value.
    /// </summary>
    internal const string KafkaProducerOrigin = "Catalog";

    internal static IServiceCollection AddKafkaMessaging(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        // The Application-layer StockLevelChangedEventProjectionHandler takes TimeProvider; pin the
        // singleton registration here so it doesn't depend on AddInbox<>()'s side-effect.
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptionsWithValidateOnStart<KafkaOptions>()
            .BindConfiguration(KafkaOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<TopicsOptions>()
            .BindConfiguration(TopicsOptions.Section)
            .ValidateDataAnnotations();

        // No ValidateDataAnnotations on the two Confluent-derived options below: their settings live
        // on the vendor base, and an attribute could only reach them by redeclaring each with `new` —
        // the trap those types document. The validators enforce them instead. The consumer options
        // keep theirs for the one range check KafkaFlow doesn't make first.
        services.AddOptionsWithValidateOnStart<SchemaRegistryOptions>()
            .BindConfiguration(SchemaRegistryOptions.Section);
        services.AddSingleton<IValidateOptions<SchemaRegistryOptions>, SchemaRegistryOptionsValidator>();

        services.AddOptionsWithValidateOnStart<AvroSerializerOptions>()
            .BindConfiguration(AvroSerializerOptions.Section);
        services.AddSingleton<IValidateOptions<AvroSerializerOptions>, AvroSerializerOptionsValidator>();

        services.AddOptionsWithValidateOnStart<InventoryStockEventsConsumerOptions>()
            .BindConfiguration(InventoryStockEventsConsumerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var consumerOptions = configuration
            .GetRequiredSection(InventoryStockEventsConsumerOptions.Section)
            .Get<InventoryStockEventsConsumerOptions>()!;
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
                    .Topic(topicsOptions.InventoryStockEvents)
                    .WithConsumerConfig(consumerOptions)
                    .WithBufferSize(consumerOptions.BufferSize)
                    .WithWorkersCount(consumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        // Middleware order — outermost to innermost.
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle(ctx => ConsumerRetry.IsRetryable(ctx.Exception))
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)))
                        .AddInbox(typeof(StockLevelChangedEvent))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<StockLevelChangedEventKafkaHandler>())
                    )
                ))
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddInbox<CatalogDbContext>();
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
