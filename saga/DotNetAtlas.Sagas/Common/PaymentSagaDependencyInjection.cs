using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Finance.PaymentSaga.Consumers;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Common;

internal static class PaymentSagaDependencyInjection
{
    extension(IRiderRegistrationConfigurator rider)
    {
        public void AddPaymentSagaConsumers()
        {
            rider.AddConsumer<PaymentRequestedConsumer>();
            rider.AddConsumer<PaymentAuthorizedConsumer>();
            rider.AddConsumer<PaymentAuthorizationFailedConsumer>();
            rider.AddConsumer<PaymentCapturedConsumer>();
            rider.AddConsumer<PaymentCaptureFailedConsumer>();
            rider.AddConsumer<PaymentVoidedConsumer>();
        }

        public void AddPaymentSagaProducers(SagaOptions options)
        {
            rider.AddProducer<Guid, RequestRefundCommand>(
                options.Topics.FinancePaymentCommands,
                (context, producerConfig) =>
                {
                    var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                    producerConfig.SetKeySerializer(
                        new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    producerConfig.SetValueSerializer(
                        new AvroSerializer<RequestRefundCommand>(schemaRegistryClient).AsSyncOverAsync());
                });

            rider.AddProducer<Guid, AuthorizePaymentCommand>(
                options.Topics.FinancePaymentCommands,
                (context, producerConfig) =>
                {
                    var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                    producerConfig.SetKeySerializer(
                        new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    producerConfig.SetValueSerializer(
                        new AvroSerializer<AuthorizePaymentCommand>(schemaRegistryClient).AsSyncOverAsync());
                });

            rider.AddProducer<Guid, CapturePaymentCommand>(
                options.Topics.FinancePaymentCommands,
                (context, producerConfig) =>
                {
                    var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                    producerConfig.SetKeySerializer(
                        new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    producerConfig.SetValueSerializer(
                        new AvroSerializer<CapturePaymentCommand>(schemaRegistryClient).AsSyncOverAsync());
                });

            rider.AddProducer<Guid, VoidPaymentCommand>(
                options.Topics.FinancePaymentCommands,
                (context, producerConfig) =>
                {
                    var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                    producerConfig.SetKeySerializer(
                        new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    producerConfig.SetValueSerializer(
                        new AvroSerializer<VoidPaymentCommand>(schemaRegistryClient).AsSyncOverAsync());
                });

            rider.AddProducer<Guid, PaymentRequestedEvent>(
                options.Topics.FinancePayments,
                (context, producerConfig) =>
                {
                    var schemaRegistryClient = context.GetRequiredService<ISchemaRegistryClient>();
                    producerConfig.SetKeySerializer(
                        new AvroSerializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
                    producerConfig.SetValueSerializer(
                        new AvroSerializer<PaymentRequestedEvent>(schemaRegistryClient).AsSyncOverAsync());
                });
        }
    }

    extension(IKafkaFactoryConfigurator kafka)
    {
        /// <summary>
        /// Configures Payment saga Kafka topic endpoints.
        /// </summary>
        public void ConfigurePaymentSagaEndpoints(IRiderRegistrationContext context,
            ISchemaRegistryClient schemaRegistry,
            SagaOptions options)
        {
            const string group = KafkaConsumerGroupBuilder.Payment;

            kafka.TopicEndpoint<Guid, PaymentRequestedEvent>(
                options.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(options.ConsumerGroup, group, "requested"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(
                        new AvroDeserializer<PaymentRequestedEvent>(schemaRegistry).AsSyncOverAsync());
                    e.ConfigureConsumer<PaymentRequestedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, PaymentAuthorizedEvent>(
                options.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(options.ConsumerGroup, group, "authorized"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(
                        new AvroDeserializer<PaymentAuthorizedEvent>(schemaRegistry).AsSyncOverAsync());
                    e.ConfigureConsumer<PaymentAuthorizedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, PaymentAuthorizationFailedEvent>(
                options.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(options.ConsumerGroup, group, "auth-failed"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(
                        new AvroDeserializer<PaymentAuthorizationFailedEvent>(schemaRegistry).AsSyncOverAsync());
                    e.ConfigureConsumer<PaymentAuthorizationFailedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, PaymentCapturedEvent>(
                options.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(options.ConsumerGroup, group, "captured"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(new AvroDeserializer<PaymentCapturedEvent>(schemaRegistry)
                        .AsSyncOverAsync());
                    e.ConfigureConsumer<PaymentCapturedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, PaymentCaptureFailedEvent>(
                options.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(options.ConsumerGroup, group, "capture-failed"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(
                        new AvroDeserializer<PaymentCaptureFailedEvent>(schemaRegistry).AsSyncOverAsync());
                    e.ConfigureConsumer<PaymentCaptureFailedConsumer>(context);
                });

            kafka.TopicEndpoint<Guid, PaymentVoidedEvent>(
                options.Topics.FinancePayments,
                KafkaConsumerGroupBuilder.Build(options.ConsumerGroup, group, "voided"),
                e =>
                {
                    e.AutoOffsetReset = AutoOffsetReset.Earliest;
                    e.SetKeyDeserializer(new AvroDeserializer<Guid>(schemaRegistry).AsSyncOverAsync());
                    e.SetValueDeserializer(new AvroDeserializer<PaymentVoidedEvent>(schemaRegistry).AsSyncOverAsync());
                    e.ConfigureConsumer<PaymentVoidedConsumer>(context);
                });
        }
    }
}
