using Inventory.Domain.StockItems.ValueObjects;

namespace Inventory.Domain.StockItems.Errors;

/// <summary>
/// Discovery surface for Inventory business-expected errors. Call sites read as
/// <c>Result.Fail(InventoryErrors.InsufficientStock(...))</c>.
/// </summary>
/// <remarks>
/// Business-expected errors only (domain returns them through
/// <see cref="FluentResults.Result"/>). Bug-class conditions (unknown
/// <c>ReservationId</c>, re-initializing a stream, adjusting below zero) throw
/// <see cref="Platform.SharedKernel.Exceptions.DataIntegrityException"/> directly from
/// the aggregate — they are not exposed here.
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
}
