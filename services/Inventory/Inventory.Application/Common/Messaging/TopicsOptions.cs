using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.Common.Messaging;

/// <summary>
/// Kafka topic names used by the Inventory bounded context — both outbound (produced
/// via the outbox) and inbound (consumed by the KafkaFlow cluster).
/// Bound from configuration section <c>Topics</c> on startup; validated eagerly
/// via <c>AddOptionsWithValidateOnStart</c>.
/// </summary>
/// <remarks>
/// Inventory publishes to two topics and consumes from three per
/// <c>events-catalog.md</c> § 3:
/// <list type="bullet">
/// <item><c>inventory.stock-events</c> — threshold-crossing
/// <c>StockLevelChangedEvent</c> signals (keyed by ProductId).</item>
/// <item><c>inventory.reservations</c> — full reservation lifecycle
/// (keyed by OrderId).</item>
/// <item><c>catalog.products</c> — <c>ProductCreatedEvent</c> from Catalog.</item>
/// <item><c>ordering.orders</c> — <c>OrderCancelledEvent</c> from Ordering.</item>
/// <item><c>inventory.reservation-commands</c> — saga commands from the Checkout saga.</item>
/// </list>
/// </remarks>
public sealed class TopicsOptions
{
    public const string Section = "Topics";
    private const int MaximumKafkaTopicLength = 249;

    /// <summary>
    /// Topic for <c>StockLevelChangedEvent</c> (threshold-crossing signals). Keyed
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
    /// Inbound topic — owned by Catalog. Carries <c>ProductCreatedEvent</c>; consumed by
    /// Inventory to initialise a fresh event-sourced stream per ProductId.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string CatalogProducts { get; set; }

    /// <summary>
    /// Inbound topic — owned by Ordering. Carries <c>OrderCancelledEvent</c>; consumed by
    /// Inventory to release still-Active reservations on order cancellation.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string OrderingOrders { get; set; }

    /// <summary>
    /// Inbound saga-command topic — owned by Inventory. Carries the 3 saga commands
    /// (<c>ReserveStockCommand</c>, <c>ConfirmReservationCommand</c>,
    /// <c>ReleaseReservationCommand</c>). Saga is the producer; Inventory is the consumer.
    /// </summary>
    [Required]
    [Length(1, MaximumKafkaTopicLength)]
    public required string InventoryReservationCommands { get; set; }

    /// <summary>
    /// Suffix appended to topic names to create Dead Letter Topics
    /// (e.g. <c>.Inventory.DLT</c>). Consumed by the KafkaFlow DLT
    /// middleware.
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
