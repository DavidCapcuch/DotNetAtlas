using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using MassTransit;
using Platform.Avro.UniversalSerDes;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga;
using SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.Consumers;

namespace SagaOrchestrators.Common.SagasDependencyInjection;

/// <summary>
/// Dependency injection extensions for configuring the
/// <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/> Kafka consumers.
/// </summary>
internal static class AlertSubscriptionPurchaseSagaDependencyInjection
{
    extension(IKafkaFactoryConfigurator kafka)
    {
        /// <summary>
        /// Configures Kafka topic endpoints and consumers for the subscription purchase saga.
        /// </summary>
        /// <param name="context">The MassTransit rider registration context.</param>
        /// <param name="schemaRegistryClient">The Confluent Schema Registry client for Avro deserialization.</param>
        /// <param name="kafkaOptions">Kafka configuration options containing topic names and consumer groups.</param>
        public void ConfigureAlertSubscriptionPurchaseSagaConsumers(IRiderRegistrationContext context,
            ISchemaRegistryClient schemaRegistryClient,
            KafkaOptions kafkaOptions)
        {
            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.OrderAlertSubscriptions,
                kafkaOptions.ConsumerGroups.AlertSubscriptionPurchaseSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer)
                            .AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionPurchaseInitiatedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.WeatherAlertSubscriptions,
                kafkaOptions.ConsumerGroups.AlertSubscriptionPurchaseSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer)
                            .AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionActivatedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.FinancePayments,
                kafkaOptions.ConsumerGroups.AlertSubscriptionPurchaseSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer)
                            .AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<AlertSubscriptionPurchasePaymentCompletedConsumer>(context);
                    consumerConfig.ConfigureConsumer<AlertSubscriptionPurchasePaymentFailedConsumer>(context);
                    consumerConfig.ConfigureConsumer<AlertSubscriptionPurchasePaymentRefundedConsumer>(context);
                });
        }
    }
}
