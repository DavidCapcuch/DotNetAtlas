using Inventory.Application.Common.ReadModels;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Inventory.Application.Common.Data;

/// <summary>
/// Application-layer port for the Inventory persistence context. Implemented
/// by <c>InventoryDbContext</c> in the Infrastructure layer. Exposes the
/// projection read-model sets required by the projection handlers + the outbox
/// plumbing inherited from <see cref="IOutboxDbContext"/> so that
/// <see cref="ITransactionalOutbox{TContext}"/> can share the same scope and
/// transaction as the event append.
/// </summary>
/// <remarks>
/// The event-store write-side (<c>stock_events</c>) is NOT surfaced here — it
/// is internal to the <c>EventStoreRepository</c>, which writes the append-only
/// rows directly on the concrete <c>InventoryDbContext</c> as part of the
/// transactional envelope described in <c>inventory.md</c> § 8.1. Application
/// handlers interact with the aggregate through the repository, never through
/// this port.
/// </remarks>
public interface IInventoryDbContext : IOutboxDbContext
{
    /// <summary>
    /// Hot-path projection — one row per <c>ProductId</c>. Upserted by
    /// <c>CurrentStockLevelsProjectionDomainEventHandler</c> for every ES event.
    /// </summary>
    DbSet<CurrentStockLevelRow> CurrentStockLevels { get; }

    /// <summary>
    /// Ops projection — one row per reservation. Inserted on
    /// <c>StockReservedEvent</c>; updated to terminal status (Confirmed,
    /// Released) by the later reservation-lifecycle events. Doubles as the
    /// driving query for the M6 <c>ReservationExpiryWorker</c>.
    /// </summary>
    DbSet<ReservationAuditRow> ReservationAudit { get; }
}
