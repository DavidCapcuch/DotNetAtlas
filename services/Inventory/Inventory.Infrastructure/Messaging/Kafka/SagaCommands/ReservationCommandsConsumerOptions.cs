using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace Inventory.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Kafka consumer configuration for the <c>inventory.reservation-commands</c>
/// saga-command topic. Bound from the
/// <c>KafkaReservationCommandsConsumer</c> section. Inherits from
/// <see cref="ConsumerConfig"/> so broker-level knobs (auto-offset-reset,
/// session-timeout, etc.) are bindable directly.
/// </summary>
/// <remarks>
/// Carries the 3 saga commands (<c>ReserveStockCommand</c>,
/// <c>ConfirmReservationCommand</c>, <c>ReleaseReservationCommand</c>) on a
/// single 3-partition topic per <c>events-catalog.md § 3</c>. Group id is
/// <c>inventory-reservation-commands</c> (distinct from the
/// <c>inventory-stock-init</c> group used by the cross-BC event consumers).
/// </remarks>
public sealed class ReservationCommandsConsumerOptions : ConsumerConfig
{
    public const string Section = "KafkaReservationCommandsConsumer";

    /// <summary>Topic carrying saga-issued reservation commands (3 partitions).</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Topic { get; set; }

    /// <summary>Consumer group id for this consumer (idempotent rebalance key).</summary>
    [Required(
        ErrorMessage = $"{nameof(GroupId)} for {nameof(ReservationCommandsConsumerOptions)} is missing",
        AllowEmptyStrings = false)]
    public new required string GroupId { get; set; }

    [Range(1, int.MaxValue)]
    public int BufferSize { get; set; } = 100;

    [Range(1, int.MaxValue)]
    public int WorkersCount { get; set; } = 1;
}
