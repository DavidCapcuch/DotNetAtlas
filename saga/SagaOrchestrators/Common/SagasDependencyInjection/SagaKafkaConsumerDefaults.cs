using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using MassTransit;
using Platform.Avro.UniversalSerDes;
using SagaOrchestrators.Common.Config.Kafka;

namespace SagaOrchestrators.Common.SagasDependencyInjection;

/// <summary>
/// Shared Kafka consumer defaults applied to every saga topic endpoint across both the
/// <c>saga-checkout</c> and <c>saga-payment-processing</c> groups. Centralising the offset-reset,
/// partition-assignment strategy, and Avro key/value deserializers keeps the two groups from
/// drifting apart — in particular the cooperative rebalance protocol (ADR-0027) must be identical
/// for both, or a Kubernetes rolling/canary deploy would still stop-the-world on the laggard group.
/// </summary>
internal static class SagaKafkaConsumerDefaults
{
    /// <summary>
    /// Applies the shared consumer configuration to a saga topic endpoint: read from earliest, the
    /// cooperative incremental rebalance protocol (<see cref="PartitionAssignmentStrategy.CooperativeSticky"/>,
    /// ADR-0027 — avoids eager "stop-the-world" rebalances during rolling/canary deploys), and the
    /// <see cref="Guid"/>-key + <see cref="ISpecificRecord"/>-value Avro deserializers.
    /// </summary>
    /// <param name="consumerConfig">The MassTransit topic-endpoint configurator to mutate.</param>
    /// <param name="schemaRegistryClient">The Confluent Schema Registry client for Avro deserialization.</param>
    /// <param name="kafkaOptions">Kafka options carrying the Avro deserializer settings.</param>
    public static void ConfigureCommon(
        this IKafkaTopicReceiveEndpointConfigurator<Guid, ISpecificRecord> consumerConfig,
        ISchemaRegistryClient schemaRegistryClient,
        KafkaOptions kafkaOptions)
    {
        consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;
        consumerConfig.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;
        consumerConfig.SetKeyDeserializer(
            new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
        consumerConfig.SetValueDeserializer(
            new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer).AsSyncOverAsync());
    }
}
