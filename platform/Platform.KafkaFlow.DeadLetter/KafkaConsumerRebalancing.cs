using Confluent.Kafka;

namespace Platform.KafkaFlow.DeadLetter;

/// <summary>
/// Single source of truth for the solution-wide consumer rebalance protocol (ADR-0027). Every
/// KafkaFlow consumer group uses the <em>cooperative incremental</em> rebalance protocol
/// (<see cref="PartitionAssignmentStrategy.CooperativeSticky"/>) instead of the librdkafka default
/// (<c>range,roundrobin</c>), so a Kubernetes rolling or canary deploy does not trigger eager
/// "stop-the-world" rebalances that revoke every partition from every consumer on each pod
/// join/leave.
/// </summary>
public static class KafkaConsumerRebalancing
{
    /// <summary>
    /// Switches the consumer to the cooperative incremental rebalance protocol by setting
    /// <see cref="ConsumerConfig.PartitionAssignmentStrategy"/> to
    /// <see cref="PartitionAssignmentStrategy.CooperativeSticky"/>. Returns the same instance so it can
    /// be chained into <c>WithConsumerConfig(...)</c>.
    /// </summary>
    /// <remarks>
    /// Under a cooperative (non-"stop-the-world") strategy KafkaFlow forces
    /// <see cref="ConsumerConfig.EnableAutoCommit"/> to <see langword="true"/> regardless of any value
    /// bound from configuration — incremental partition revocation is committed by librdkafka's
    /// rebalance callback rather than KafkaFlow's eager committer. KafkaFlow still stores offsets
    /// manually (<c>EnableAutoOffsetStore = false</c>, <c>StoreOffset()</c> only after a message is
    /// processed), so delivery stays <strong>at-least-once</strong>; the only change is a slightly
    /// wider duplicate-redelivery window (bounded by <c>auto.commit.interval.ms</c>, default 5 s),
    /// which the inbox dedup middleware absorbs (<c>conventions.md §6</c>). This is why the BC
    /// appsettings no longer carry an <c>EnableAutoCommit</c> knob — it would be a silent no-op.
    /// </remarks>
    /// <param name="config">The consumer configuration to mutate.</param>
    /// <returns>The same <paramref name="config"/> instance, for chaining.</returns>
    public static ConsumerConfig WithCooperativeRebalancing(this ConsumerConfig config)
    {
        config.PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky;
        return config;
    }
}
