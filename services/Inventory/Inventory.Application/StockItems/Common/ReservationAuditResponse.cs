using Inventory.Domain.StockItems.ValueObjects;

namespace Inventory.Application.StockItems.Common;

/// <summary>
/// Public read-side projection of <c>inventory.reservation_audit</c>. Returned
/// by <see cref="GetReservationById.GetReservationByIdQuery"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="Inventory.Application.Common.ReadModels.ReservationAuditRow"/>
/// 1:1 — admin tooling needs every field including the terminal-state fields
/// (<see cref="ResolvedAtUtc"/>, <see cref="ReleaseReason"/>) for status
/// dashboards.
/// </remarks>
public sealed class ReservationAuditResponse
{
    public required Guid ReservationId { get; init; }

    public required Guid ProductId { get; init; }

    public required Guid OrderId { get; init; }

    public required int Quantity { get; init; }

    public required ReservationStatus Status { get; init; }

    public required DateTimeOffset ReservedAtUtc { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public DateTimeOffset? ResolvedAtUtc { get; init; }

    public ReleaseReason? ReleaseReason { get; init; }
}
