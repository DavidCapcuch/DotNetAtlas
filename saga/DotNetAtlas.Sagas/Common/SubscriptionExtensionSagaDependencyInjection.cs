using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Consumers;
using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;
using Weather.Alerts;

namespace DotNetAtlas.Sagas.Common;

internal static class SubscriptionExtensionSagaDependencyInjection
{
    extension(IRiderRegistrationConfigurator rider)
    {
        public void AddSubscriptionExtensionSagaConsumers()
        {
            rider.AddConsumer<AlertSubscriptionExtensionInitiatedConsumer>();
            rider.AddConsumer<SubscriptionExtendedConsumer>();
            rider.AddConsumer<SubscriptionExtensionPaymentRefundedConsumer>();
            rider.AddConsumer<SubscriptionExtensionPaymentCompletedConsumer>();
            rider.AddConsumer<SubscriptionExtensionPaymentFailedConsumer>();
        }

        public void AddSubscriptionExtensionSagaProducers(SagaOptions options)
        {
            rider.AddProducer<Guid, ExtendSubscriptionCommand>(
                options.Topics.WeatherAlertsCommands,
                (context, producerConfig) =>
                {
                    var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                    producerConfig.SetKeySerializer(
                        new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    producerConfig.SetValueSerializer(
                        new AvroSerializer<ExtendSubscriptionCommand>(schemaRegistryClient).AsSyncOverAsync());
                });
        }
    }

    extension(IKafkaFactoryConfigurator kafka)
    {
        public void ConfigureSubscriptionExtensionSagaEndpoints(IRiderRegistrationContext context,
            ISchemaRegistryClient schemaRegistry,
            SagaOptions options)
        {
            const string group = KafkaConsumerGroupBuilder.SubscriptionExtension;

            kafka.TopicEndpoint<Guid, SubscriptionExtensionInitiatedSagaEvent>(
                options.Topics.OrderAlertSubscriptions,
                KafkaConsumerGroupBuilder.Build(options.ConsumerGroup, group, "initiated"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(
                        new AvroDeserializer<SubscriptionExtensionInitiatedSagaEvent>(schemaRegistry)
                            .AsSyncOverAsync());
                    e.ConfigureConsumer<AlertSubscriptionExtensionInitiatedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, SubscriptionExtendedSagaEvent>(
                options.Topics.WeatherAlerts,
                KafkaConsumerGroupBuilder.Build(options.ConsumerGroup, group, "extended"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(
                        new AvroDeserializer<SubscriptionExtendedSagaEvent>(schemaRegistry).AsSyncOverAsync());
                    e.ConfigureConsumer<SubscriptionExtendedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, SubscriptionExtensionPaymentCompletedSagaEvent>(
                options.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(options.ConsumerGroup, group, "payment-completed"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(
                        new AvroDeserializer<SubscriptionExtensionPaymentCompletedSagaEvent>(schemaRegistry)
                            .AsSyncOverAsync());
                    e.ConfigureConsumer<SubscriptionExtensionPaymentCompletedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, SubscriptionExtensionPaymentFailedSagaEvent>(
                options.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(options.ConsumerGroup, group, "payment-failed"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(new AvroDeserializer<SubscriptionExtensionPaymentFailedSagaEvent>(schemaRegistry)
                        .AsSyncOverAsync());
                    e.ConfigureConsumer<SubscriptionExtensionPaymentFailedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, PaymentRefundedEvent>(
                options.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(options.ConsumerGroup, group, "payment-refunded"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(new AvroDeserializer<PaymentRefundedEvent>(schemaRegistry)
                        .AsSyncOverAsync());
                    e.ConfigureConsumer<SubscriptionExtensionPaymentRefundedConsumer>(context);
                });
        }
    }
}
