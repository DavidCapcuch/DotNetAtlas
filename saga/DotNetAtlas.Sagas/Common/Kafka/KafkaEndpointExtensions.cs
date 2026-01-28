using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Consumers;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Consumers;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Consumers;
using MassTransit;

// Avro event aliases
using AvroAlertSubscriptionExtensionInitiatedEvent = Order.AlertSubscriptions.AlertSubscriptionExtensionInitiatedEvent;
using AvroAlertSubscriptionPurchaseInitiatedEvent = Order.AlertSubscriptions.AlertSubscriptionPurchaseInitiatedEvent;
using AvroPaymentAuthorizationFailedEvent = Finance.Payments.PaymentAuthorizationFailedEvent;
using AvroPaymentAuthorizedEvent = Finance.Payments.PaymentAuthorizedEvent;
using AvroPaymentCapturedEvent = Finance.Payments.PaymentCapturedEvent;
using AvroPaymentCaptureFailedEvent = Finance.Payments.PaymentCaptureFailedEvent;
using AvroPaymentCompletedEvent = Finance.Payments.PaymentCompletedEvent;
using AvroPaymentFailedEvent = Finance.Payments.PaymentFailedEvent;
using AvroPaymentRefundedEvent = Finance.Payments.PaymentRefundedEvent;
using AvroPaymentRequestedEvent = Finance.Payments.PaymentRequestedEvent;
using AvroPaymentVoidedEvent = Finance.Payments.PaymentVoidedEvent;
using AvroSubscriptionActivatedEvent = Weather.Alerts.SubscriptionActivatedEvent;
using AvroSubscriptionExtendedEvent = Weather.Alerts.SubscriptionExtendedEvent;

// Consumer aliases to disambiguate same-named consumers in different namespaces
using ExtPaymentCompletedConsumer =
    DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Consumers.PaymentCompletedConsumer;
using ExtPaymentFailedConsumer =
    DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Consumers.PaymentFailedConsumer;
using ExtPaymentRefundedConsumer =
    DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Consumers.PaymentRefundedConsumer;
using PurchasePaymentCompletedConsumer =
    DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Consumers.PaymentCompletedConsumer;
using PurchasePaymentFailedConsumer =
    DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Consumers.PaymentFailedConsumer;
using PurchasePaymentRefundedConsumer =
    DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Consumers.PaymentRefundedConsumer;

namespace DotNetAtlas.Sagas.Common.Kafka;

/// <summary>
/// Extension methods for configuring Kafka topic endpoints.
/// </summary>
public static class KafkaEndpointExtensions
{
    /// <summary>
    /// Configures Purchase saga Kafka topic endpoints.
    /// </summary>
    public static void ConfigurePurchaseSagaEndpoints(
        this IKafkaFactoryConfigurator kafka,
        IRiderRegistrationContext context,
        ISchemaRegistryClient schemaRegistry,
        SagaOptions options)
    {
        var group = KafkaConsumerGroups.Purchase;

        // Order: AlertSubscriptionPurchaseInitiatedEvent (starts the Purchase saga)
        kafka.TopicEndpoint<Guid, AvroAlertSubscriptionPurchaseInitiatedEvent>(
            options.Topics.OrderAlertSubscriptions,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "initiated"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(
                    new AvroDeserializer<AvroAlertSubscriptionPurchaseInitiatedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<AlertSubscriptionPurchaseInitiatedConsumer>(context);
            });

        // Weather: SubscriptionActivatedEvent
        kafka.TopicEndpoint<Guid, AvroSubscriptionActivatedEvent>(
            options.Topics.WeatherAlerts,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "activated"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(
                    new AvroDeserializer<AvroSubscriptionActivatedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<WeatherSubscriptionActivatedConsumer>(context);
            });

        // Finance: PaymentCompletedEvent (for purchase saga)
        kafka.TopicEndpoint<Guid, AvroPaymentCompletedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "payment-completed"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(new AvroDeserializer<AvroPaymentCompletedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<PurchasePaymentCompletedConsumer>(context);
            });

        // Finance: PaymentFailedEvent (for purchase saga)
        kafka.TopicEndpoint<Guid, AvroPaymentFailedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "payment-failed"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(new AvroDeserializer<AvroPaymentFailedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<PurchasePaymentFailedConsumer>(context);
            });

        // Finance: PaymentRefundedEvent (for compensation)
        kafka.TopicEndpoint<Guid, AvroPaymentRefundedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "payment-refunded"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(new AvroDeserializer<AvroPaymentRefundedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<PurchasePaymentRefundedConsumer>(context);
            });
    }

    /// <summary>
    /// Configures Extension saga Kafka topic endpoints.
    /// </summary>
    public static void ConfigureExtensionSagaEndpoints(
        this IKafkaFactoryConfigurator kafka,
        IRiderRegistrationContext context,
        ISchemaRegistryClient schemaRegistry,
        SagaOptions options)
    {
        var group = KafkaConsumerGroups.Extension;

        // Order: AlertSubscriptionExtensionInitiatedEvent (starts the Extension saga)
        kafka.TopicEndpoint<Guid, AvroAlertSubscriptionExtensionInitiatedEvent>(
            options.Topics.OrderAlertSubscriptions,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "initiated"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(
                    new AvroDeserializer<AvroAlertSubscriptionExtensionInitiatedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<AlertSubscriptionExtensionInitiatedConsumer>(context);
            });

        // Weather: SubscriptionExtendedEvent
        kafka.TopicEndpoint<Guid, AvroSubscriptionExtendedEvent>(
            options.Topics.WeatherAlerts,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "extended"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(
                    new AvroDeserializer<AvroSubscriptionExtendedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<SubscriptionExtendedConsumer>(context);
            });

        // Finance: PaymentCompletedEvent (for extension saga)
        kafka.TopicEndpoint<Guid, AvroPaymentCompletedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "payment-completed"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(new AvroDeserializer<AvroPaymentCompletedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<ExtPaymentCompletedConsumer>(context);
            });

        // Finance: PaymentFailedEvent (for extension saga)
        kafka.TopicEndpoint<Guid, AvroPaymentFailedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "payment-failed"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(new AvroDeserializer<AvroPaymentFailedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<ExtPaymentFailedConsumer>(context);
            });

        // Finance: PaymentRefundedEvent (for extension saga compensation)
        kafka.TopicEndpoint<Guid, AvroPaymentRefundedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "payment-refunded"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(new AvroDeserializer<AvroPaymentRefundedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<ExtPaymentRefundedConsumer>(context);
            });
    }

    /// <summary>
    /// Configures Payment saga Kafka topic endpoints.
    /// </summary>
    public static void ConfigurePaymentSagaEndpoints(
        this IKafkaFactoryConfigurator kafka,
        IRiderRegistrationContext context,
        ISchemaRegistryClient schemaRegistry,
        SagaOptions options)
    {
        var group = KafkaConsumerGroups.Payment;

        // Finance: PaymentRequestedEvent
        kafka.TopicEndpoint<Guid, AvroPaymentRequestedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "requested"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(new AvroDeserializer<AvroPaymentRequestedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<PaymentRequestedConsumer>(context);
            });

        // Finance: PaymentAuthorizedEvent
        kafka.TopicEndpoint<Guid, AvroPaymentAuthorizedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "authorized"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(new AvroDeserializer<AvroPaymentAuthorizedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<PaymentAuthorizedConsumer>(context);
            });

        // Finance: PaymentAuthorizationFailedEvent
        kafka.TopicEndpoint<Guid, AvroPaymentAuthorizationFailedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "auth-failed"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(
                    new AvroDeserializer<AvroPaymentAuthorizationFailedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<PaymentAuthorizationFailedConsumer>(context);
            });

        // Finance: PaymentCapturedEvent
        kafka.TopicEndpoint<Guid, AvroPaymentCapturedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "captured"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(new AvroDeserializer<AvroPaymentCapturedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<PaymentCapturedConsumer>(context);
            });

        // Finance: PaymentCaptureFailedEvent
        kafka.TopicEndpoint<Guid, AvroPaymentCaptureFailedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "capture-failed"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(
                    new AvroDeserializer<AvroPaymentCaptureFailedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<PaymentCaptureFailedConsumer>(context);
            });

        // Finance: PaymentVoidedEvent
        kafka.TopicEndpoint<Guid, AvroPaymentVoidedEvent>(
            options.Topics.FinancePayments,
            KafkaConsumerGroups.Build(options.ConsumerGroup, group, "voided"),
            e =>
            {
                e.AutoOffsetReset = AutoOffsetReset.Earliest;
                e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                e.SetValueDeserializer(new AvroDeserializer<AvroPaymentVoidedEvent>(schemaRegistry).AsSyncOverAsync());
                e.ConfigureConsumer<PaymentVoidedConsumer>(context);
            });
    }
}

