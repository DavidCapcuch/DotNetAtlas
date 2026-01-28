using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Consumers;
using Finance.Payments;
using MassTransit;
using Order.AlertSubscriptions;
using Weather.Alerts;

namespace DotNetAtlas.Sagas.Common;

internal static class SubscriptionPurchaseSagaDependencyInjection
{
    extension(IRiderRegistrationConfigurator rider)
    {
        public void AddSubscriptionPurchaseSagaConsumers()
        {
            rider.AddConsumer<AlertSubscriptionPurchaseInitiatedConsumer>();
            rider.AddConsumer<SubscriptionActivatedConsumer>();
            rider.AddConsumer<SubscriptionPurchasePaymentRefundedConsumer>();
            rider.AddConsumer<SubscriptionPurchasePaymentCompletedConsumer>();
            rider.AddConsumer<SubscriptionPurchasePaymentFailedConsumer>();
        }

        public void AddSubscriptionPurchaseSagaProducers(SagaOptions options)
        {
            rider.AddProducer<Guid, ActivateSubscriptionCommand>(
                options.Topics.WeatherAlertsCommands,
                (context, producerConfig) =>
                {
                    var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                    producerConfig.SetKeySerializer(
                        new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    producerConfig.SetValueSerializer(
                        new AvroSerializer<ActivateSubscriptionCommand>(schemaRegistryClient).AsSyncOverAsync());
                });
        }
    }

    extension(IKafkaFactoryConfigurator kafka)
    {
        public void ConfigureSubscriptionPurchaseSagaEndpoints(IRiderRegistrationContext context,
            ISchemaRegistryClient schemaRegistry,
            SagaOptions sagaOptions)
        {
            var group = KafkaConsumerGroupBuilder.SubscriptionPurchase;

            kafka.TopicEndpoint<Guid, AlertSubscriptionPurchaseInitiatedEvent>(
                sagaOptions.Topics.OrderAlertSubscriptions,
                KafkaConsumerGroupBuilder.Build(sagaOptions.ConsumerGroup, group, "initiated"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(
                        new AvroDeserializer<AlertSubscriptionPurchaseInitiatedEvent>(schemaRegistry)
                            .AsSyncOverAsync());
                    e.ConfigureConsumer<AlertSubscriptionPurchaseInitiatedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, SubscriptionActivatedEvent>(
                sagaOptions.Topics.WeatherAlerts,
                KafkaConsumerGroupBuilder.Build(sagaOptions.ConsumerGroup, group, "activated"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(
                        new AvroDeserializer<SubscriptionActivatedEvent>(schemaRegistry).AsSyncOverAsync());
                    e.ConfigureConsumer<SubscriptionActivatedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, PaymentCompletedEvent>(
                sagaOptions.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(sagaOptions.ConsumerGroup, group, "payment-completed"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(
                        new AvroDeserializer<PaymentCompletedEvent>(schemaRegistry).AsSyncOverAsync());
                    e.ConfigureConsumer<SubscriptionPurchasePaymentCompletedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, PaymentFailedEvent>(
                sagaOptions.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(sagaOptions.ConsumerGroup, group, "payment-failed"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(new AvroDeserializer<PaymentFailedEvent>(schemaRegistry).AsSyncOverAsync());
                    e.ConfigureConsumer<SubscriptionPurchasePaymentFailedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, PaymentRefundedEvent>(
                sagaOptions.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(sagaOptions.ConsumerGroup, group, "payment-refunded"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(new AvroDeserializer<PaymentRefundedEvent>(schemaRegistry)
                        .AsSyncOverAsync());
                    e.ConfigureConsumer<SubscriptionPurchasePaymentRefundedConsumer>(context);
                });
        }
    }
}
