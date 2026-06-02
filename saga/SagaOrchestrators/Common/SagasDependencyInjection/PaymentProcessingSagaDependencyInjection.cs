using Avro.Specific;
using Confluent.SchemaRegistry;
using MassTransit;
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
        /// Configures Kafka topic endpoints and consumers for the payment processing saga. Two
        /// topic subscriptions under one consumer group per ADR-0023:
        /// <list type="bullet">
        /// <item><c>payments.transactions</c> (event log) — Payments-BC-emitted lifecycle facts
        /// the saga reacts to (Authorized / AuthorizationFailed / Captured / CaptureFailed /
        /// Voided).</item>
        /// <item><c>payments.payment-commands</c> (command stream) — imperative
        /// <c>RequestPaymentCommand</c> sent by the Checkout saga to initiate sub-saga processing,
        /// plus the ADR-0026 capture-pivot handshake (<c>ApproveCaptureCommand</c> /
        /// <c>AbortCaptureCommand</c>) the Checkout saga sends after confirming stock + order.
        /// Shares the topic with Payments-BC consumers of <c>AuthorizePaymentCommand</c> / etc.;
        /// MassTransit dispatches by message type so cross-consumption is clean.</item>
        /// </list>
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
                    consumerConfig.ConfigureCommon(schemaRegistryClient, kafkaOptions);

                    consumerConfig.ConfigureConsumer<PaymentVoidedConsumer>(context);
                    consumerConfig.ConfigureConsumer<PaymentAuthorizationFailedConsumer>(context);
                    consumerConfig.ConfigureConsumer<PaymentAuthorizedConsumer>(context);
                    consumerConfig.ConfigureConsumer<PaymentCapturedConsumer>(context);
                    consumerConfig.ConfigureConsumer<PaymentCaptureFailedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.PaymentsPaymentCommands,
                kafkaOptions.ConsumerGroups.PaymentProcessingSaga,
                consumerConfig =>
                {
                    consumerConfig.ConfigureCommon(schemaRegistryClient, kafkaOptions);

                    consumerConfig.ConfigureConsumer<RequestPaymentCommandConsumer>(context);
                    consumerConfig.ConfigureConsumer<ApproveCaptureCommandConsumer>(context);
                    consumerConfig.ConfigureConsumer<AbortCaptureCommandConsumer>(context);
                });
        }
    }
}
