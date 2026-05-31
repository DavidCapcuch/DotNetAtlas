namespace Inventory.Application.Common.Messaging;

/// <summary>
/// Compile-time names for every Kafka topic + consumer group Inventory
/// publishes to or consumes from. The values are LOCKED contract per the
/// Inventory BC.
/// </summary>
/// <remarks>
/// The constants are intentionally <c>const</c> rather than <c>static readonly</c>
/// so they can be used in attributes / switch expressions if a future need arises.
/// </remarks>
public static class KafkaTopicNames
{
    /// <summary>
    /// <c>inventory.stock-events</c> — threshold-crossing <c>StockLevelChangedEvent</c>
    /// signals, keyed by ProductId, 3 partitions, retention.ms=-1 (infinite).
    /// </summary>
    public const string InventoryStockEvents = "inventory.stock-events";

    /// <summary>
    /// <c>inventory.reservations</c> — full reservation lifecycle events
    /// (StockReserved, StockReservationFailed, ReservationConfirmed,
    /// ReservationReleased), keyed by OrderId, 6 partitions (saga fan-out
    /// invariant), retention.ms=-1 (infinite).
    /// </summary>
    public const string InventoryReservations = "inventory.reservations";

    /// <summary>
    /// <c>inventory.reservation-commands</c> — saga-command topic consumed by
    /// the saga-command Kafka handlers (Reserve / Confirm / Release).
    /// 3 partitions, retention.ms=604800000 (7 days per D-9).
    /// </summary>
    public const string InventoryReservationCommands = "inventory.reservation-commands";

    /// <summary>
    /// DLT suffix appended to topic names by the KafkaFlow DLT middleware.
    /// </summary>
    public const string DltTopicSuffix = ".Inventory.DLT";

    /// <summary>
    /// Inventory's sole Kafka consumer group, shared across every topic Inventory
    /// subscribes to per the one-group-per-service rule in
    /// <c>docs/bc-design/events-catalog.md § 3.1</c>. Kept here as a
    /// design-document constant; the runtime values are read from
    /// <c>appsettings.json</c> (<c>KafkaCatalogProductsConsumer.GroupId</c>,
    /// <c>KafkaOrderingOrdersConsumer.GroupId</c>,
    /// <c>KafkaReservationCommandsConsumer.GroupId</c>) and must all match this
    /// constant.
    /// </summary>
    public const string ConsumerGroup = "inventory-group";
}
