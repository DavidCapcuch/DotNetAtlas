using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Sagas.Common.AvroDeserialization;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Consumers;
using MassTransit;

namespace DotNetAtlas.Sagas.Common;

internal static class SubscriptionExtensionSagaDependencyInjection
{
    extension(IKafkaFactoryConfigurator kafka)
    {
        public void ConfigureSubscriptionExtensionSagaConsumers(IRiderRegistrationContext context,
            ISchemaRegistryClient schemaRegistryClient,
            SagaKafkaOptions kafkaOptions)
        {
            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.OrderAlertSubscriptions,
                kafkaOptions.ConsumerGroups.SubscriptionExtensionSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer).AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionExtensionInitiatedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.WeatherAlertSubscriptions,
                kafkaOptions.ConsumerGroups.SubscriptionExtensionSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer).AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionExtendedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.FinancePayments,
                kafkaOptions.ConsumerGroups.SubscriptionExtensionSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer).AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionExtensionPaymentCompletedConsumer>(context);
                    consumerConfig.ConfigureConsumer<AlertSubscriptionExtensionPaymentFailedConsumer>(context);
                    consumerConfig.ConfigureConsumer<AlertSubscriptionExtensionPaymentRefundedConsumer>(context);
                });
        }
    }
}
