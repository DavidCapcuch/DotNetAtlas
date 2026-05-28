using Inventory.Application.Common.Messaging;
using Inventory.Infrastructure.Messaging.Kafka.Config;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using Inventory.Infrastructure.Messaging.Kafka.StockInit;
using Inventory.Infrastructure.Persistence.Database;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
/// with three consumers — saga commands on <c>inventory.reservation-commands</c>,
/// Catalog products on <c>catalog.products</c>, and Ordering orders on
/// <c>ordering.orders</c> — and the cluster-level DLT producer.
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// Service origin identifier written to the <c>origin</c> Kafka header by
    /// the outbox relay and the cluster's DLT producer.
    /// </summary>
    internal const string KafkaProducerOrigin = "Inventory";

    internal static IServiceCollection AddMessaging(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<KafkaOptions>()
            .BindConfiguration(KafkaOptions.Section)
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

        var catalogProductsOptions = configuration
            .GetRequiredSection(CatalogProductsConsumerOptions.Section)
            .Get<CatalogProductsConsumerOptions>()!;

        var orderingOrdersOptions = configuration
            .GetRequiredSection(OrderingOrdersConsumerOptions.Section)
            .Get<OrderingOrdersConsumerOptions>()!;

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
                // Consumer 1: saga commands on inventory.reservation-commands
                // (group: inventory-reservation-commands).
                .AddConsumer(consumer => consumer
                    .Topic(reservationCommandsOptions.Topic)
                    .WithConsumerConfig(reservationCommandsOptions)
                    .WithBufferSize(reservationCommandsOptions.BufferSize)
                    .WithWorkersCount(reservationCommandsOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        // Middleware order -> outermost to innermost.
                        .AddCorrelationIdConsumerMiddleware()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle<DbUpdateException>()
                            .Handle<NpgsqlException>()
                            .Handle<TimeoutException>()
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
                // Consumer 2: Catalog products on catalog.products
                // (group: inventory-stock-init -- shared with Ordering consumer).
                .AddConsumer(consumer => consumer
                    .Topic(catalogProductsOptions.Topic)
                    .WithConsumerConfig(catalogProductsOptions)
                    .WithBufferSize(catalogProductsOptions.BufferSize)
                    .WithWorkersCount(catalogProductsOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        .AddCorrelationIdConsumerMiddleware()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle<DbUpdateException>()
                            .Handle<NpgsqlException>()
                            .Handle<TimeoutException>()
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)))
                        .AddInbox(typeof(AvroProductCreatedEvent))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<ProductCreatedEventKafkaHandler>())))
                // Consumer 3: Ordering orders on ordering.orders
                // (same group as Catalog consumer: inventory-stock-init).
                .AddConsumer(consumer => consumer
                    .Topic(orderingOrdersOptions.Topic)
                    .WithConsumerConfig(orderingOrdersOptions)
                    .WithBufferSize(orderingOrdersOptions.BufferSize)
                    .WithWorkersCount(orderingOrdersOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        .AddCorrelationIdConsumerMiddleware()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle<DbUpdateException>()
                            .Handle<NpgsqlException>()
                            .Handle<TimeoutException>()
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
