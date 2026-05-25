using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using MassTransit;
using Platform.Avro.UniversalSerDes;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Payments.PaymentProcessingSaga;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Consumers;

namespace SagaOrchestrators.Common.SagasDependencyInjection;

/// <summary>
/// Dependency injection extensions for configuring the
/// <see cref="PaymentProcessingSagaOrchestrator"/> Kafka consumers.
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
            KafkaOptions kafkaOptions)
        {
            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.PaymentsTransactions,
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
