using Invoicing.Application.Common.Messaging;
using Invoicing.Infrastructure.Messaging.Kafka.Config;
using Invoicing.Infrastructure.Messaging.Kafka.Notifications;
using Invoicing.Infrastructure.Messaging.Kafka.Projections;
using Invoicing.Infrastructure.Persistence.Database;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.Retry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notifications.Email;
using Npgsql;
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
/// DI wiring for Kafka — the four M6 enrichment-projection consumers
/// (<c>OrderConfirmedEvent</c>, <c>OrderCancelledEvent</c>,
/// <c>PaymentCapturedEvent</c>, <c>PaymentRefundedEvent</c>) and the inbox
/// dedup adapter against <see cref="InvoicingDbContext"/>. M7 will add the
/// transactional-outbox writer + outbox configuration when issuance command
/// handlers and external <c>InvoiceIssued</c> publishers land.
/// </summary>
internal static class MessagingDependencyInjection
{
    /// <summary>
    /// Service origin identifier written to the <c>origin</c> Kafka header by
    /// the producer-headers middleware (used today by the DLT producer; M7's
    /// outbox relay <c>outbox-relay-invoicing</c> reads its origin from
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

        // InvoicingTopicsOptions registration moved to AddInvoicingApplication (M7) so the
        // outbox publishers in the Application layer can read it without depending on
        // Infrastructure-namespace types. Consumer setup below binds it directly from
        // configuration to extract topic names at startup.
        services.AddOptionsWithValidateOnStart<OrderingOrdersConsumerOptions>()
            .BindConfiguration(OrderingOrdersConsumerOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<PaymentsTransactionsConsumerOptions>()
            .BindConfiguration(PaymentsTransactionsConsumerOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<NotificationsEmailEventsConsumerOptions>()
            .BindConfiguration(NotificationsEmailEventsConsumerOptions.Section)
            .ValidateDataAnnotations();

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;

        var topicsOptions = configuration
            .GetRequiredSection(InvoicingTopicsOptions.Section)
            .Get<InvoicingTopicsOptions>()!;

        var orderingConsumerOptions = configuration
            .GetRequiredSection(OrderingOrdersConsumerOptions.Section)
            .Get<OrderingOrdersConsumerOptions>()!;

        var paymentsConsumerOptions = configuration
            .GetRequiredSection(PaymentsTransactionsConsumerOptions.Section)
            .Get<PaymentsTransactionsConsumerOptions>()!;

        var notificationsEmailEventsConsumerOptions = configuration
            .GetRequiredSection(NotificationsEmailEventsConsumerOptions.Section)
            .Get<NotificationsEmailEventsConsumerOptions>()!;

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
                    .Topic(orderingConsumerOptions.Topic)
                    .WithConsumerConfig(orderingConsumerOptions)
                    .WithBufferSize(orderingConsumerOptions.BufferSize)
                    .WithWorkersCount(orderingConsumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        // Middleware order — outermost to innermost.
                        .AddCorrelationIdConsumerMiddleware()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle<DbUpdateException>()
                            .Handle<NpgsqlException>()
                            .Handle<TimeoutException>()
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
                    .Topic(paymentsConsumerOptions.Topic)
                    .WithConsumerConfig(paymentsConsumerOptions)
                    .WithBufferSize(paymentsConsumerOptions.BufferSize)
                    .WithWorkersCount(paymentsConsumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        .AddCorrelationIdConsumerMiddleware()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle<DbUpdateException>()
                            .Handle<NpgsqlException>()
                            .Handle<TimeoutException>()
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
                    .Topic(notificationsEmailEventsConsumerOptions.Topic)
                    .WithConsumerConfig(notificationsEmailEventsConsumerOptions)
                    .WithBufferSize(notificationsEmailEventsConsumerOptions.BufferSize)
                    .WithWorkersCount(notificationsEmailEventsConsumerOptions.WorkersCount)
                    .AddMiddlewares(middlewares => middlewares
                        .AddSchemaRegistryAvroDeserializer()
                        .AddCorrelationIdConsumerMiddleware()
                        .AddDeadLetter()
                        .RetryForever(config => config
                            .Handle<DbUpdateException>()
                            .Handle<NpgsqlException>()
                            .Handle<TimeoutException>()
                            .WithTimeBetweenTriesPlan(
                                TimeSpan.FromMilliseconds(500),
                                TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2),
                                TimeSpan.FromSeconds(5)))
                        .AddInbox(typeof(EmailNotificationSentEvent))
                        .AddTypedHandlers(handlers => handlers
                            .WithHandlerLifetime(InstanceLifetime.Scoped)
                            .AddHandler<EmailNotificationSentEventKafkaHandler>())))
            )
            .UseMicrosoftLog()
            .AddOpenTelemetryInstrumentation());

        services.AddInbox<InvoicingDbContext>();

        // M7 — transactional outbox for InvoiceIssuedEvent / InvoiceCancelledEvent /
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
