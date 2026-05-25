using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using MassTransit;
using Platform.Avro.UniversalSerDes;
using SagaOrchestrators.Checkout.CheckoutSaga;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Common.Config.Kafka;

namespace SagaOrchestrators.Common.SagasDependencyInjection;

/// <summary>
/// Dependency injection extensions for configuring the
/// <see cref="CheckoutSagaOrchestrator"/> Kafka consumers per
/// docs/bc-design/checkout-saga.md § 8 (12 adapters across 4 topics, all under the
/// <c>saga-checkout</c> consumer group). Mirrors
/// <see cref="PaymentProcessingSagaDependencyInjection"/>.
/// </summary>
internal static class CheckoutSagaDependencyInjection
{
    extension(IKafkaFactoryConfigurator kafka)
    {
        /// <summary>
        /// Configures Kafka topic endpoints and consumers for the Checkout saga. The
        /// <c>payments.transactions</c> topic is shared with PaymentProcessingSaga; the
        /// <c>saga-checkout</c> consumer group keeps offsets independent so the two sagas
        /// receive every event independently per ADR-0001.
        /// </summary>
        /// <param name="context">The MassTransit rider registration context.</param>
        /// <param name="schemaRegistryClient">The Confluent Schema Registry client for Avro deserialization.</param>
        /// <param name="kafkaOptions">Kafka configuration options containing topic names and consumer groups.</param>
        public void ConfigureCheckoutSagaConsumers(IRiderRegistrationContext context,
            ISchemaRegistryClient schemaRegistryClient,
            KafkaOptions kafkaOptions)
        {
            var checkoutGroup = kafkaOptions.ConsumerGroups.CheckoutSaga;

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.BasketSessions,
                checkoutGroup,
                consumerConfig =>
                {
                    ConfigureCommon(consumerConfig, schemaRegistryClient, kafkaOptions);
                    consumerConfig.ConfigureConsumer<BasketCheckoutInitiatedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.OrderingOrders,
                checkoutGroup,
                consumerConfig =>
                {
                    ConfigureCommon(consumerConfig, schemaRegistryClient, kafkaOptions);
                    consumerConfig.ConfigureConsumer<OrderCreatedConsumer>(context);
                    consumerConfig.ConfigureConsumer<OrderConfirmedConsumer>(context);
                    consumerConfig.ConfigureConsumer<OrderCancelledConsumer>(context);
                    consumerConfig.ConfigureConsumer<OrderFailedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.InventoryReservations,
                checkoutGroup,
                consumerConfig =>
                {
                    ConfigureCommon(consumerConfig, schemaRegistryClient, kafkaOptions);
                    consumerConfig.ConfigureConsumer<StockReservedConsumer>(context);
                    consumerConfig.ConfigureConsumer<StockReservationFailedConsumer>(context);
                    consumerConfig.ConfigureConsumer<ReservationConfirmedConsumer>(context);
                    consumerConfig.ConfigureConsumer<ReservationReleasedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, ISpecificRecord>(
                kafkaOptions.Topics.PaymentsPayments,
                checkoutGroup,
                consumerConfig =>
                {
                    ConfigureCommon(consumerConfig, schemaRegistryClient, kafkaOptions);
                    consumerConfig.ConfigureConsumer<PaymentCompletedCheckoutConsumer>(context);
                    consumerConfig.ConfigureConsumer<PaymentFailedCheckoutConsumer>(context);
                    consumerConfig.ConfigureConsumer<PaymentRefundedCheckoutConsumer>(context);
                });
        }
    }

    private static void ConfigureCommon(IKafkaTopicReceiveEndpointConfigurator<Guid, ISpecificRecord> consumerConfig,
        ISchemaRegistryClient schemaRegistryClient,
        KafkaOptions kafkaOptions)
    {
        consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
        consumerConfig.SetKeyDeserializer(
            new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
        consumerConfig.SetValueDeserializer(
            new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer).AsSyncOverAsync());
    }
}
