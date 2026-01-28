using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Consumers;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Consumers;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Consumers;
using Finance.Payments;
using MassTransit;
using AvroActivateSubscriptionCommand = Weather.Alerts.ActivateSubscriptionCommand;
using AvroExtendSubscriptionCommand = Weather.Alerts.ExtendSubscriptionCommand;
using AvroPaymentRequestedEvent = Finance.Payments.PaymentRequestedEvent;
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
/// Kafka consumer group name constants for saga consumers.
/// </summary>
public static class KafkaConsumerGroups
{
    /// <summary>
    /// Consumer group suffix for purchase saga consumers.
    /// </summary>
    public const string Purchase = "purchase";

    /// <summary>
    /// Consumer group suffix for extension saga consumers.
    /// </summary>
    public const string Extension = "extension";

    /// <summary>
    /// Consumer group suffix for payment saga consumers.
    /// </summary>
    public const string Payment = "payment";

    /// <summary>
    /// Builds a consumer group name from the base group and saga-specific suffix.
    /// </summary>
    /// <param name="baseGroup">The base consumer group name from options.</param>
    /// <param name="sagaType">The saga type (e.g., "purchase", "extension", "payment").</param>
    /// <param name="eventName">The event name suffix.</param>
    /// <returns>The full consumer group name.</returns>
    public static string Build(string baseGroup, string sagaType, string eventName)
        => $"{baseGroup}-{sagaType}-{eventName}";
}

/// <summary>
/// Extension methods for configuring Kafka riders for saga consumers.
/// </summary>
public static class KafkaRiderExtensions
{
    /// <summary>
    /// Adds all Kafka consumers for the saga service.
    /// </summary>
    public static void AddSagaKafkaConsumers(this IRiderRegistrationConfigurator rider)
    {
        rider.AddPurchaseSagaConsumers();
        rider.AddExtensionSagaConsumers();
        rider.AddPaymentSagaConsumers();
    }

    /// <summary>
    /// Adds all Kafka producers for the saga service.
    /// </summary>
    public static void AddSagaKafkaProducers(
        this IRiderRegistrationConfigurator rider,
        SagaOptions options)
    {
        rider.AddFinancePaymentProducers(options);
        rider.AddWeatherAlertProducers(options);
    }

    /// <summary>
    /// Configures all Kafka topic endpoints for the saga service.
    /// </summary>
    public static void ConfigureSagaTopicEndpoints(
        this IKafkaFactoryConfigurator kafkaFactoryConfigurator,
        IRiderRegistrationContext registrationContext,
        SagaOptions options)
    {
        var schemaRegistryClient = registrationContext.GetRequiredService<ISchemaRegistryClient>();

        kafkaFactoryConfigurator.ConfigurePurchaseSagaEndpoints(registrationContext, schemaRegistryClient, options);
        kafkaFactoryConfigurator.ConfigureExtensionSagaEndpoints(registrationContext, schemaRegistryClient, options);
        kafkaFactoryConfigurator.ConfigurePaymentSagaEndpoints(registrationContext, schemaRegistryClient, options);
    }

    /// <summary>
    /// Adds purchase saga Kafka consumers.
    /// </summary>
    public static void AddPurchaseSagaConsumers(this IRiderRegistrationConfigurator rider)
    {
        rider.AddConsumer<AlertSubscriptionPurchaseInitiatedConsumer>();
        rider.AddConsumer<WeatherSubscriptionActivatedConsumer>();
        rider.AddConsumer<PurchasePaymentRefundedConsumer>();
        rider.AddConsumer<PurchasePaymentCompletedConsumer>();
        rider.AddConsumer<PurchasePaymentFailedConsumer>();
    }

    /// <summary>
    /// Adds extension saga Kafka consumers.
    /// </summary>
    public static void AddExtensionSagaConsumers(this IRiderRegistrationConfigurator rider)
    {
        rider.AddConsumer<AlertSubscriptionExtensionInitiatedConsumer>();
        rider.AddConsumer<SubscriptionExtendedConsumer>();
        rider.AddConsumer<ExtPaymentRefundedConsumer>();
        rider.AddConsumer<ExtPaymentCompletedConsumer>();
        rider.AddConsumer<ExtPaymentFailedConsumer>();
    }

    /// <summary>
    /// Adds payment saga Kafka consumers.
    /// </summary>
    public static void AddPaymentSagaConsumers(this IRiderRegistrationConfigurator rider)
    {
        rider.AddConsumer<PaymentRequestedConsumer>();
        rider.AddConsumer<PaymentAuthorizedConsumer>();
        rider.AddConsumer<PaymentAuthorizationFailedConsumer>();
        rider.AddConsumer<PaymentCapturedConsumer>();
        rider.AddConsumer<PaymentCaptureFailedConsumer>();
        rider.AddConsumer<PaymentVoidedConsumer>();
    }

    /// <summary>
    /// Adds Finance payment command producers.
    /// </summary>
    public static void AddFinancePaymentProducers(
        this IRiderRegistrationConfigurator rider,
        SagaOptions options)
    {
        rider.AddProducer<Guid, RequestRefundCommand>(
            options.Topics.FinancePaymentCommands,
            (context, producerConfig) =>
            {
                var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                producerConfig.SetKeySerializer(
                    new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                producerConfig.SetValueSerializer(
                    new AvroSerializer<RequestRefundCommand>(schemaRegistryClient).AsSyncOverAsync());
            });

        rider.AddProducer<Guid, AuthorizePaymentCommand>(
            options.Topics.FinancePaymentCommands,
            (context, producerConfig) =>
            {
                var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                producerConfig.SetKeySerializer(
                    new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                producerConfig.SetValueSerializer(
                    new AvroSerializer<AuthorizePaymentCommand>(schemaRegistryClient).AsSyncOverAsync());
            });

        rider.AddProducer<Guid, CapturePaymentCommand>(
            options.Topics.FinancePaymentCommands,
            (context, producerConfig) =>
            {
                var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                producerConfig.SetKeySerializer(
                    new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                producerConfig.SetValueSerializer(
                    new AvroSerializer<CapturePaymentCommand>(schemaRegistryClient).AsSyncOverAsync());
            });

        rider.AddProducer<Guid, VoidPaymentCommand>(
            options.Topics.FinancePaymentCommands,
            (context, producerConfig) =>
            {
                var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                producerConfig.SetKeySerializer(
                    new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                producerConfig.SetValueSerializer(
                    new AvroSerializer<VoidPaymentCommand>(schemaRegistryClient).AsSyncOverAsync());
            });

        // Producer for PaymentRequestedEvent - published by Purchase/Extension saga to initiate Payment saga
        rider.AddProducer<Guid, AvroPaymentRequestedEvent>(
            options.Topics.FinancePayments,
            (context, producerConfig) =>
            {
                var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                producerConfig.SetKeySerializer(
                    new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                producerConfig.SetValueSerializer(
                    new AvroSerializer<AvroPaymentRequestedEvent>(schemaRegistryClient).AsSyncOverAsync());
            });
    }

    /// <summary>
    /// Adds Weather Alert command producers.
    /// </summary>
    public static void AddWeatherAlertProducers(
        this IRiderRegistrationConfigurator rider,
        SagaOptions options)
    {
        // Producer for ActivateSubscriptionCommand - published by Purchase saga to activate subscription
        rider.AddProducer<Guid, AvroActivateSubscriptionCommand>(
            options.Topics.WeatherAlertsCommands,
            (context, producerConfig) =>
            {
                var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                producerConfig.SetKeySerializer(
                    new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                producerConfig.SetValueSerializer(
                    new AvroSerializer<AvroActivateSubscriptionCommand>(schemaRegistryClient).AsSyncOverAsync());
            });

        // Producer for ExtendSubscriptionCommand - published by Extension saga to extend subscription
        rider.AddProducer<Guid, AvroExtendSubscriptionCommand>(
            options.Topics.WeatherAlertsCommands,
            (context, producerConfig) =>
            {
                var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                producerConfig.SetKeySerializer(
                    new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                producerConfig.SetValueSerializer(
                    new AvroSerializer<AvroExtendSubscriptionCommand>(schemaRegistryClient).AsSyncOverAsync());
            });
    }
}

