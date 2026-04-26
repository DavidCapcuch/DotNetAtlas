using Inventory.Domain.StockItems.ValueObjects;
using Platform.SharedKernel.Errors;

namespace Inventory.Domain.StockItems.Errors;

/// <summary>
/// Discovery surface for Inventory business-expected errors. Call sites read as
/// <c>Result.Fail(InventoryErrors.InsufficientStock(...))</c>.
/// </summary>
/// <remarks>
/// Business-expected errors only (domain returns them through
/// <see cref="FluentResults.Result"/>). Aggregate-internal bug-class conditions
/// — Confirm/Release against an unknown ReservationId, re-initializing a
/// stream, adjusting below zero — throw
/// <see cref="Platform.SharedKernel.Exceptions.DataIntegrityException"/>
/// directly from the aggregate and are not exposed here. Read-side 404s for
/// projection rows missing for an admin lookup ARE business-expected and use
/// the <see cref="StockItemNotFound"/> / <see cref="ReservationNotFound"/>
/// factories below.
/// </remarks>
public static class InventoryErrors
{
    public static InsufficientStockError InsufficientStock(Guid productId, int requested, int available)
        => new(productId, requested, available);

    public static ConcurrencyError Concurrency(Guid streamId, int expectedVersion)
        => new(streamId, expectedVersion);

    public static ReservationNotActiveError ReservationNotActive(
        Guid productId,
        Guid reservationId,
        ReservationStatus currentStatus)
        => new(productId, reservationId, currentStatus);

    public static NotFoundError StockItemNotFound(Guid productId)
        => new("StockItem", productId, "Inventory.StockItem.NotFound");

    public static NotFoundError ReservationNotFound(Guid reservationId)
        => new("Reservation", reservationId, "Inventory.Reservation.NotFound");
}
