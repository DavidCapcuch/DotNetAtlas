using Confluent.Kafka;
using Invoicing.Application.Common.Messaging;
using Invoicing.Infrastructure.Messaging.Kafka.Config;
using Invoicing.Infrastructure.Messaging.Kafka.Notifications;
using Invoicing.Infrastructure.Messaging.Kafka.Projections;
using Invoicing.Infrastructure.Persistence.Database;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Notifications;
using Platform.KafkaFlow.DeadLetter;
using Platform.KafkaFlow.DeadLetter.Common;
using Platform.KafkaFlow.Inbox.EFCore.Common;
using Platform.KafkaFlow.ProducerHeaders;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using AvroOrderCancelledEvent = Ordering.Orders.OrderCancelledEvent;
using AvroOrderConfirmedEvent = Ordering.Orders.OrderConfirmedEvent;
using AvroPaymentCapturedEvent = Payments.Transactions.PaymentCapturedEvent;
using AvroPaymentRefundedEvent = Payments.Transactions.PaymentRefundedEvent;

namespace Invoicing.Infrastructure.Common;

/// <summary>
/// DI wiring for Kafka — three consumer registrations (<c>TopicsOptions.OrderingOrders</c>,
/// <c>TopicsOptions.PaymentsTransactions</c>, <c>TopicsOptions.NotificationsNotifyEvents</c>)
/// carrying five typed handlers in total (four enrichment-projection handlers for
/// <c>OrderConfirmedEvent</c>/<c>OrderCancelledEvent</c>/<c>PaymentCapturedEvent</c>/
/// <c>PaymentRefundedEvent</c>, plus one delivery-tracking handler for
/// <c>NotificationDeliveryStatusChangedEvent</c>), the inbox dedup adapter against
/// <see cref="InvoicingDbContext"/>, and the transactional-outbox writer + outbox
/// configuration for the issuance command handlers' external <c>InvoiceIssued</c> /
/// <c>InvoiceCancelled</c> / <c>CreditNoteIssued</c> publishers.
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// Service origin identifier written to the <c>origin</c> Kafka header by
    /// the producer-headers middleware (used by the DLT producer; the outbox relay
    /// <c>outbox-relay-invoicing</c> reads its origin from
    /// <c>OUTBOX_MESSAGE_ORIGIN</c> and the two MUST agree).
    /// </summary>
    internal const string KafkaProducerOrigin = "Invoicing";

    internal static IServiceCollection AddKafkaMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Make the projection consumers' TimeProvider dependency explicit;
        // AddInbox<>() also registers it as a side-effect, but that's an implicit coupling.
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

        services.AddOptionsWithValidateOnStart<OrderingOrdersConsumerOptions>()
            .BindConfiguration(OrderingOrdersConsumerOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<PaymentsTransactionsConsumerOptions>()
            .BindConfiguration(PaymentsTransactionsConsumerOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<NotificationsNotifyEventsConsumerOptions>()
            .BindConfiguration(NotificationsNotifyEventsConsumerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var topicsOptions = configuration
            .GetRequiredSection(TopicsOptions.Section)
            .Get<TopicsOptions>()!;

        var orderingConsumerOptions = configuration
            .GetRequiredSection(OrderingOrdersConsumerOptions.Section)
            .Get<OrderingOrdersConsumerOptions>()!;
        orderingConsumerOptions.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;

        var paymentsConsumerOptions = configuration
            .GetRequiredSection(PaymentsTransactionsConsumerOptions.Section)
            .Get<PaymentsTransactionsConsumerOptions>()!;
        paymentsConsumerOptions.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;

        var notificationsNotifyEventsConsumerOptions = configuration
            .GetRequiredSection(NotificationsNotifyEventsConsumerOptions.Section)
            .Get<NotificationsNotifyEventsConsumerOptions>()!;
        notificationsNotifyEventsConsumerOptions.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;

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
                    .Topic(topicsOptions.OrderingOrders)
                    .WithConsumerConfig(orderingConsumerOptions)
                    .WithBufferSize(orderingConsumerOptions.BufferSize)
                    .WithWorkersCount(orderingConsumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        // Middleware order — outermost to innermost.
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle(ctx => ConsumerRetry.IsRetryable(ctx.Exception))
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500),
                                TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2),
                                TimeSpan.FromSeconds(5)))
                        .AddInbox(typeof(AvroOrderConfirmedEvent), typeof(AvroOrderCancelledEvent))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<OrderConfirmedInvoiceProjectionKafkaHandler>()
                            .AddHandler<OrderCancelledCreditNoteProjectionKafkaHandler>())))
                .AddConsumer(consumer => consumer
                    .Topic(topicsOptions.PaymentsTransactions)
                    .WithConsumerConfig(paymentsConsumerOptions)
                    .WithBufferSize(paymentsConsumerOptions.BufferSize)
                    .WithWorkersCount(paymentsConsumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle(ctx => ConsumerRetry.IsRetryable(ctx.Exception))
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500),
                                TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2),
                                TimeSpan.FromSeconds(5)))
                        .AddInbox(typeof(AvroPaymentCapturedEvent), typeof(AvroPaymentRefundedEvent))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<PaymentCapturedInvoiceProjectionKafkaHandler>()
                            .AddHandler<PaymentRefundedCreditNoteProjectionKafkaHandler>())))
                .AddConsumer(consumer => consumer
                    .Topic(topicsOptions.NotificationsNotifyEvents)
                    .WithConsumerConfig(notificationsNotifyEventsConsumerOptions)
                    .WithBufferSize(notificationsNotifyEventsConsumerOptions.BufferSize)
                    .WithWorkersCount(notificationsNotifyEventsConsumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle(ctx => ConsumerRetry.IsRetryable(ctx.Exception))
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500),
                                TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2),
                                TimeSpan.FromSeconds(5)))
                        .AddInbox(typeof(NotificationDeliveryStatusChangedEvent))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<NotificationDeliveryStatusChangedEventKafkaHandler>())))
            )
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddInbox<InvoicingDbContext>();

        // Transactional outbox for InvoiceIssuedEvent / InvoiceCancelledEvent /
        // CreditNoteIssuedEvent. The outbox-relay-invoicing container reads from
        // invoicing.OutboxMessages and publishes to invoicing.invoices using the
        // Avro serializer + schema-registry settings bound from Kafka:* below;
        // the relay's OUTBOX_MESSAGE_ORIGIN env var must agree with KafkaProducerOrigin.
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
