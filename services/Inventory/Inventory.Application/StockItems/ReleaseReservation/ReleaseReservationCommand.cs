using Inventory.Domain.StockItems.ValueObjects;
using Platform.CQRS;

namespace Inventory.Application.StockItems.ReleaseReservation;

/// <summary>
/// Drops an <c>Active</c> reservation without confirming it. Three sources:
/// saga compensation, the TTL expiry worker, customer/admin cancel.
/// <see cref="Reason"/> distinguishes the cause; it propagates unchanged into
/// the external <c>ReservationReleasedEvent</c>.
/// </summary>
public sealed record ReleaseReservationCommand : ICommand
{
    public required Guid ReservationId { get; init; }

    public required Guid ProductId { get; init; }

    public required ReleaseReason Reason { get; init; }

    public required DateTimeOffset OccurredOnUtc { get; init; }
}
