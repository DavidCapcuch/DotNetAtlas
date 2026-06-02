using Confluent.Kafka;
using Inventory.Application.Common.Messaging;
using Inventory.Infrastructure.Messaging.Kafka.Config;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.Infrastructure.Messaging.Kafka.StockInit;
using Inventory.Infrastructure.Persistence.Database;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.KafkaFlow.DeadLetter;
using Platform.KafkaFlow.DeadLetter.Common;
using Platform.KafkaFlow.Inbox.EFCore.Common;
using Platform.KafkaFlow.ProducerHeaders;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using AvroConfirmReservationCommand = Inventory.Reservations.ConfirmReservationCommand;
using AvroOrderCancelledEvent = Ordering.Orders.OrderCancelledEvent;
using AvroProductCreatedEvent = Catalog.Products.ProductCreatedEvent;
using AvroReleaseReservationCommand = Inventory.Reservations.ReleaseReservationCommand;
using AvroReserveStockCommand = Inventory.Reservations.ReserveStockCommand;

namespace Inventory.Infrastructure.Common;

/// <summary>
/// Messaging DI for the Inventory bounded context. Wires the producer-side outbox
/// + inbox adapter against <c>InventoryDbContext</c> plus the KafkaFlow cluster
/// with three consumers — saga commands on <c>TopicsOptions.InventoryReservationCommands</c>,
/// Catalog products on <c>TopicsOptions.CatalogProducts</c>, and Ordering orders on
/// <c>TopicsOptions.OrderingOrders</c> — and the cluster-level DLT producer.
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// Service origin identifier written to the <c>origin</c> Kafka header by
    /// the outbox relay and the cluster's DLT producer.
    /// </summary>
    internal const string KafkaProducerOrigin = "Inventory";

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

        services.AddOptionsWithValidateOnStart<ReservationCommandsConsumerOptions>()
            .BindConfiguration(ReservationCommandsConsumerOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<CatalogProductsConsumerOptions>()
            .BindConfiguration(CatalogProductsConsumerOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<OrderingOrdersConsumerOptions>()
            .BindConfiguration(OrderingOrdersConsumerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var reservationCommandsOptions = configuration
            .GetRequiredSection(ReservationCommandsConsumerOptions.Section)
            .Get<ReservationCommandsConsumerOptions>()!;
        reservationCommandsOptions.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;

        var catalogProductsOptions = configuration
            .GetRequiredSection(CatalogProductsConsumerOptions.Section)
            .Get<CatalogProductsConsumerOptions>()!;
        catalogProductsOptions.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;

        var orderingOrdersOptions = configuration
            .GetRequiredSection(OrderingOrdersConsumerOptions.Section)
            .Get<OrderingOrdersConsumerOptions>()!;
        orderingOrdersOptions.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;

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
                // Consumer 1: saga commands on TopicsOptions.InventoryReservationCommands.
                // Group id is inventory-group — Inventory's sole consumer group
                // (one-group-per-service rule, events-catalog.md § 3.1).
                .AddConsumer(consumer => consumer
                    .Topic(topicsOptions.InventoryReservationCommands)
                    .WithConsumerConfig(reservationCommandsOptions)
                    .WithBufferSize(reservationCommandsOptions.BufferSize)
                    .WithWorkersCount(reservationCommandsOptions.WorkersCount)
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
                            typeof(AvroReserveStockCommand),
                            typeof(AvroConfirmReservationCommand),
                            typeof(AvroReleaseReservationCommand))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<ReserveStockCommandKafkaHandler>()
                            .AddHandler<ConfirmReservationCommandKafkaHandler>()
                            .AddHandler<ReleaseReservationCommandKafkaHandler>())))
                // Consumer 2: Catalog products on TopicsOptions.CatalogProducts.
                // Group id is inventory-group — Inventory's sole consumer group
                // (one-group-per-service rule, events-catalog.md § 3.1).
                .AddConsumer(consumer => consumer
                    .Topic(topicsOptions.CatalogProducts)
                    .WithConsumerConfig(catalogProductsOptions)
                    .WithBufferSize(catalogProductsOptions.BufferSize)
                    .WithWorkersCount(catalogProductsOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        .AddCorrelationIdConsumerMiddleware()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle(ctx => ConsumerRetry.IsRetryable(ctx.Exception))
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)))
                        .AddInbox(typeof(AvroProductCreatedEvent))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<ProductCreatedEventKafkaHandler>())))
                // Consumer 3: Ordering orders on TopicsOptions.OrderingOrders.
                // Group id is inventory-group — Inventory's sole consumer group
                // (one-group-per-service rule, events-catalog.md § 3.1).
                .AddConsumer(consumer => consumer
                    .Topic(topicsOptions.OrderingOrders)
                    .WithConsumerConfig(orderingOrdersOptions)
                    .WithBufferSize(orderingOrdersOptions.BufferSize)
                    .WithWorkersCount(orderingOrdersOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        .AddCorrelationIdConsumerMiddleware()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle(ctx => ConsumerRetry.IsRetryable(ctx.Exception))
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)))
                        .AddInbox(typeof(AvroOrderCancelledEvent))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<OrderCancelledEventKafkaHandler>()))))
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddInbox<InventoryDbContext>();

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
