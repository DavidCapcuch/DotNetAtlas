using Confluent.Kafka;
using EShop.BFF.Infrastructure.Messaging.Config;
using KafkaFlow;
using KafkaFlow.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BFF.Infrastructure.Messaging;

/// <summary>
/// DI wiring for the BFF's Kafka cache-invalidation consumer (group <c>bff-group</c>, bff.md § 2.2).
/// </summary>
/// <remarks>
/// Consumer-only by design — no producer, no inbox, no DLT (the dispatch contract's "no Kafka producer /
/// only consumer", and the "no new topics" boundary forbids a <c>*.Bff.DLT</c> topic): invalidation is an
/// idempotent <c>RemoveByTag</c>, so at-least-once redelivery is safe and the soft TTL backstops a missed
/// eviction. One consumer subscribes to all three published-language topics; KafkaFlow routes each
/// deserialized Avro event to the typed invalidator that handles it.
/// </remarks>
internal static class BffMessagingDependencyInjection
{
    internal static IServiceCollection AddBffMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptionsWithValidateOnStart<BffKafkaOptions>()
            .BindConfiguration(BffKafkaOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<BffTopicsOptions>()
            .BindConfiguration(BffTopicsOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<BffCacheInvalidationConsumerOptions>()
            .BindConfiguration(BffCacheInvalidationConsumerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(BffKafkaOptions.Section)
            .Get<BffKafkaOptions>()!;

        var consumerOptions = configuration
            .GetRequiredSection(BffCacheInvalidationConsumerOptions.Section)
            .Get<BffCacheInvalidationConsumerOptions>()!;
        consumerOptions.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;

        var topicsOptions = configuration
            .GetRequiredSection(BffTopicsOptions.Section)
            .Get<BffTopicsOptions>()!;

        services.AddKafka(kafka => kafka
            .AddCluster(cluster => cluster
                .WithBrokers(kafkaOptions.Brokers)
                .WithSchemaRegistry(config => config.Url = kafkaOptions.SchemaRegistry.Url)
                .AddConsumer(consumer => consumer
                    .Topics(
                        topicsOptions.CatalogProducts,
                        topicsOptions.CatalogCategories,
                        topicsOptions.InventoryStockEvents)
                    .WithConsumerConfig(consumerOptions)
                    .WithBufferSize(consumerOptions.BufferSize)
                    .WithWorkersCount(consumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        // Consume-only pipeline: Avro deserialize → dispatch. No inbox (idempotent), no
                        // DLT (no producer / no new topics) — bff.md § 2.2.
                        .AddSchemaRegistryAvroDeserializer()
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<ProductEventCacheInvalidator>()
                            .AddHandler<CategoryEventCacheInvalidator>()
                            .AddHandler<StockEventCacheInvalidator>()))))
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        return services;
    }
}
