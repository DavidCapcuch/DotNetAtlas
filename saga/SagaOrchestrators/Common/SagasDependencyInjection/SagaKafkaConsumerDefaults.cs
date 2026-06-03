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
/// drifting apart.
/// </summary>
internal static class SagaKafkaConsumerDefaults
{
    /// <summary>
    /// Applies the shared consumer configuration to a saga topic endpoint: read from earliest, the
    /// eager <see cref="PartitionAssignmentStrategy.Range"/> assignor (see the inline note for why not
    /// CooperativeSticky), and the <see cref="Guid"/>-key + <see cref="ISpecificRecord"/>-value Avro
    /// deserializers.
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

        // Eager Range, NOT CooperativeSticky (despite ADR-0027's solution-wide cooperative default):
        // MassTransit's Kafka rider wires EAGER Assign/Unassign rebalance callbacks — its
        // SetPartitionsAssignedHandler returns the partition set (=> Confluent.Kafka eager Assign()),
        // with no IncrementalAssign anywhere (true in 8.5.7..8.5.9..master). The cooperative incremental
        // protocol REQUIRES IncrementalAssign/IncrementalUnassign, so under cooperative-sticky the eager
        // assign is rejected, the consumer stops heartbeating, and the broker evicts it every
        // session.timeout.ms (~45s) — an unbounded rejoin loop that never lets the bus start (issue #306).
        // MassTransit added cooperative support in v9 (commercial); the OSS 8.x line pinned here cannot,
        // so the saga uses the eager protocol where eager callbacks are correct. CooperativeSticky still
        // applies to the KafkaFlow BC consumers, whose stack does implement incremental handling.
        consumerConfig.PartitionAssignmentStrategy = PartitionAssignmentStrategy.Range;
        consumerConfig.SetKeyDeserializer(
            new AvroDeserializer<Guid>(schemaRegistryClient).AsSyncOverAsync());
        consumerConfig.SetValueDeserializer(
            new UniversalAvroDeserializer(schemaRegistryClient, kafkaOptions.AvroDeserializer).AsSyncOverAsync());
    }
}
