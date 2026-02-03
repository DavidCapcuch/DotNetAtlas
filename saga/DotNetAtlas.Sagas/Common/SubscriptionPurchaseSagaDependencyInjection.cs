using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Sagas.Common.AvroDeserialization;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Consumers;
using MassTransit;

namespace DotNetAtlas.Sagas.Common;

internal static class SubscriptionPurchaseSagaDependencyInjection
{
    extension(IKafkaFactoryConfigurator kafka)
    {
        public void ConfigureSubscriptionPurchaseSagaConsumers(IRiderRegistrationContext context,
            ISchemaRegistryClient schemaRegistryClient,
            SagaKafkaOptions kafkaOptions)
        {
            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.OrderAlertSubscriptions,
                kafkaOptions.ConsumerGroups.SubscriptionPurchaseSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer).AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionPurchaseInitiatedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.WeatherAlertSubscriptions,
                kafkaOptions.ConsumerGroups.SubscriptionPurchaseSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer).AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionActivatedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.FinancePayments,
                kafkaOptions.ConsumerGroups.SubscriptionPurchaseSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer).AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionPurchasePaymentCompletedConsumer>(context);
                    consumerConfig.ConfigureConsumer<AlertSubscriptionPurchasePaymentFailedConsumer>(context);
                    consumerConfig.ConfigureConsumer<AlertSubscriptionPurchasePaymentRefundedConsumer>(context);
                });
        }
    }
}
