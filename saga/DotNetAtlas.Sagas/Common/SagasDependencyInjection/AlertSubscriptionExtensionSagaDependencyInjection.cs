using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Avro.UniversalSerDes;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Common.Config.Kafka;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Consumers;
using MassTransit;

namespace DotNetAtlas.Sagas.Common.SagasDependencyInjection;

/// <summary>
/// Dependency injection extensions for configuring the
/// <see cref="AlertSubscriptionExtensionSagaOrchestrator"/> Kafka consumers.
/// </summary>
internal static class AlertSubscriptionExtensionSagaDependencyInjection
{
    extension(IKafkaFactoryConfigurator kafka)
    {
        /// <summary>
        /// Configures Kafka topic endpoints and consumers for the subscription extension saga.
        /// </summary>
        /// <param name="context">The MassTransit rider registration context.</param>
        /// <param name="schemaRegistryClient">The Confluent Schema Registry client for Avro deserialization.</param>
        /// <param name="kafkaOptions">Kafka configuration options containing topic names and consumer groups.</param>
        public void ConfigureAlertSubscriptionExtensionSagaConsumers(IRiderRegistrationContext context,
            ISchemaRegistryClient schemaRegistryClient,
            KafkaOptions kafkaOptions)
        {
            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.OrderAlertSubscriptions,
                kafkaOptions.ConsumerGroups.AlertSubscriptionExtensionSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer)
                            .AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionExtensionInitiatedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.WeatherAlertSubscriptions,
                kafkaOptions.ConsumerGroups.AlertSubscriptionExtensionSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer)
                            .AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionExtendedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.FinancePayments,
                kafkaOptions.ConsumerGroups.AlertSubscriptionExtensionSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer)
                            .AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionExtensionPaymentCompletedConsumer>(context);
                    consumerConfig.ConfigureConsumer<AlertSubscriptionExtensionPaymentFailedConsumer>(context);
                    consumerConfig.ConfigureConsumer<AlertSubscriptionExtensionPaymentRefundedConsumer>(context);
                });
        }
    }
}
