using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Sagas.Common.AvroDeserialization;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Consumers;
using MassTransit;

namespace DotNetAtlas.Sagas.Common.SagasDependencyInjection;

/// <summary>
/// Dependency injection extensions for configuring the
/// <see cref="Finance.PaymentProcessingSaga.PaymentProcessingSaga"/> Kafka consumers.
/// </summary>
internal static class PaymentProcessingSagaDependencyInjection
{
    extension(IKafkaFactoryConfigurator kafka)
    {
        /// <summary>
        /// Configures Kafka topic endpoints and consumers for the payment processing saga.
        /// </summary>
        /// <param name="context">The MassTransit rider registration context.</param>
        /// <param name="schemaRegistryClient">The Confluent Schema Registry client for Avro deserialization.</param>
        /// <param name="kafkaOptions">Kafka configuration options containing topic names and consumer groups.</param>
        public void ConfigurePaymentSagaConsumers(IRiderRegistrationContext context,
            ISchemaRegistryClient schemaRegistryClient,
            SagaKafkaOptions kafkaOptions)
        {
            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.FinancePayments,
                kafkaOptions.ConsumerGroups.PaymentProcessingSaga,
                consumerConfig =>
                {
                    consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
                    consumerConfig.SetKeyDeserializer(
                        new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    consumerConfig.SetValueDeserializer(
                        new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer).AsSyncOverAsync());

                    consumerConfig.ConfigureConsumer<PaymentRequestedConsumer>(context);
                    consumerConfig.ConfigureConsumer<PaymentVoidedConsumer>(context);
                    consumerConfig.ConfigureConsumer<PaymentAuthorizationFailedConsumer>(context);
                    consumerConfig.ConfigureConsumer<PaymentAuthorizedConsumer>(context);
                    consumerConfig.ConfigureConsumer<PaymentCapturedConsumer>(context);
                    consumerConfig.ConfigureConsumer<PaymentCaptureFailedConsumer>(context);
                });
        }
    }
}
