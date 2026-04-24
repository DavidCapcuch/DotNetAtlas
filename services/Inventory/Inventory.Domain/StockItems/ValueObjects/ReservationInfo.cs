using Platform.SharedKernel.Base;

namespace Inventory.Domain.StockItems.ValueObjects;

/// <summary>
/// In-memory projection of a single reservation on the rehydrated aggregate.
/// Immutable — status transitions produce new instances via record <c>with</c>
/// expressions inside the aggregate's reducers.
/// </summary>
public sealed record ReservationInfo(
    ReservationId ReservationId,
    Guid ProductId,
    int Quantity,
    Guid OrderId,
    DateTimeOffset ReservedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    ReservationStatus Status) : ValueObject;
