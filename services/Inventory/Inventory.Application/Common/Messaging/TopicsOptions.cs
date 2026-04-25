using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.Common.Messaging;

/// <summary>
/// Kafka topic names the Inventory Application layer emits to via the outbox.
/// Bound from configuration section <c>Topics</c> on startup; validated eagerly
/// via <c>AddOptionsWithValidateOnStart</c>.
/// </summary>
/// <remarks>
/// Inventory publishes to two topics per <c>events-catalog.md</c> § 3:
/// <list type="bullet">
/// <item><c>inventory.stock-events</c> — threshold-crossing
/// <c>StockLevelChanged</c> signals (keyed by ProductId).</item>
/// <item><c>inventory.reservations</c> — full reservation lifecycle
/// (keyed by OrderId).</item>
/// </list>
/// The saga-command topic <c>inventory.reservation-commands</c> is
/// consumer-side only and is not listed here.
/// </remarks>
public sealed class TopicsOptions
{
    public const string Section = "Topics";
    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Topic for <c>StockLevelChanged</c> (threshold-crossing signals). Keyed
    /// by <c>ProductId</c>; consumed by Catalog's IsSellable projection and
    /// future low-stock alerting.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string InventoryStockEvents { get; set; }

    /// <summary>
    /// Topic for reservation lifecycle events — <c>StockReservedEvent</c>,
    /// <c>StockReservationFailedEvent</c>, <c>ReservationConfirmedEvent</c>,
    /// <c>ReservationReleasedEvent</c>. Keyed by <c>OrderId</c>; consumed by
    /// the Checkout saga and optional notifications.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string InventoryReservations { get; set; }

    /// <summary>
    /// Suffix appended to topic names to create Dead Letter Topics
    /// (e.g. <c>.Inventory.DLT</c>). Consumed by the M5 KafkaFlow DLT
    /// middleware.
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
