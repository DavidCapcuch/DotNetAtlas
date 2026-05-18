namespace Inventory.Application.Common.Messaging;

/// <summary>
/// Compile-time names for every Kafka topic + consumer group Inventory
/// publishes to or consumes from. The values are LOCKED contract per the
/// Inventory BC <c>&lt;contract&gt;</c> and ADR-0004 — a runtime typo in
/// <c>appsettings.json</c> would silently create a new topic on prod, so an
/// architecture test (<c>KafkaTopicNamesMatchAppSettingsTests</c>) asserts
/// each appsettings value equals the corresponding constant here.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the pattern Ordering adopted in its M5 milestone. The constants
/// are intentionally <c>const</c> rather than <c>static readonly</c> so they
/// can be used in attributes / switch expressions if a future need arises.
/// </para>
/// </remarks>
public static class KafkaTopicNames
{
    /// <summary>
    /// <c>inventory.stock-events</c> — threshold-crossing <c>StockLevelChanged</c>
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
    /// Consumer-group name shared between the Catalog-products and
    /// Ordering-orders consumers (deviation #1 from <c>events-catalog.md § E.10</c>
    /// per <c>inventory.md:70</c>).
    /// </summary>
    public const string StockInitConsumerGroup = "inventory-stock-init";

    /// <summary>
    /// Consumer-group name for the saga-command consumer.
    /// </summary>
    public const string ReservationCommandsConsumerGroup = "inventory-reservation-commands";
}
