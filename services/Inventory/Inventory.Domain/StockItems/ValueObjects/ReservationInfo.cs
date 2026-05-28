using Platform.SharedKernel.Base;

namespace Inventory.Domain.StockItems.ValueObjects;

/// <summary>
/// In-memory projection of a single reservation on the rehydrated aggregate.
/// Immutable — status transitions produce new instances via record <c>with</c>
/// expressions inside the aggregate's reducers; the <c>with</c> path uses the
/// compiler-synthesized copy ctor and is unaffected by the private primary ctor below.
/// </summary>
/// <remarks>
/// Per the Inventory architecture-test rule, value objects must not expose a public
/// constructor — callers route through the static <see cref="Create"/> factory so the
/// construction site is greppable and any future invariant validation has a single place
/// to live.
/// </remarks>
public sealed record ReservationInfo : ValueObject
{
    private ReservationInfo()
    {
    }

    public required ReservationId ReservationId { get; init; }

    public Guid ProductId { get; init; }

    public int Quantity { get; init; }

    public Guid OrderId { get; init; }

    public DateTimeOffset ReservedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public ReservationStatus Status { get; init; }

    public static ReservationInfo Create(
        ReservationId reservationId,
        Guid productId,
        int quantity,
        Guid orderId,
        DateTimeOffset reservedAtUtc,
        DateTimeOffset expiresAtUtc,
        ReservationStatus status) => new()
        {
            ReservationId = reservationId,
            ProductId = productId,
            Quantity = quantity,
            OrderId = orderId,
            ReservedAtUtc = reservedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            Status = status,
        };
}
