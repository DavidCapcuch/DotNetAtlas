using Inventory.Application.Common.ReadModels;
using Inventory.Reservations;
using Platform.SharedKernel.Exceptions;
using DomainReleaseReason = Inventory.Domain.StockItems.ValueObjects.ReleaseReason;
using InternalReservationReleasedEvent = Inventory.Domain.StockItems.Events.ReservationReleasedEvent;

namespace Inventory.Application.StockItems.ReleaseReservation;

/// <summary>
/// Maps internal <see cref="InternalReservationReleasedEvent"/> + the
/// <see cref="ReservationAuditRow"/> → external Avro
/// <see cref="Inventory.Reservations.ReservationReleasedEvent"/>. The internal
/// event carries the domain <c>ReleaseReason</c> enum; this mapper converts
/// to the Avro-generated enum of the same name.
/// </summary>
internal static class ReservationReleasedMapper
{
    public static Inventory.Reservations.ReservationReleasedEvent ToReservationReleasedEvent(
        this InternalReservationReleasedEvent source,
        ReservationAuditRow audit) =>
        new()
        {
            ProductId = source.ProductId,
            ReservationId = source.ReservationId,
            OrderId = audit.OrderId,
            ReleaseReason = MapReason(source.ReleaseReason),
            ReleasedAtUtc = source.OccurredOnUtc.UtcDateTime,
        };

    internal static ReleaseReason MapReason(DomainReleaseReason domainReason) => domainReason switch
    {
        DomainReleaseReason.Compensation => ReleaseReason.Compensation,
        DomainReleaseReason.Expiry => ReleaseReason.Expiry,
        DomainReleaseReason.Cancellation => ReleaseReason.Cancellation,
        _ => throw new DataIntegrityException(
            "Inventory.UnknownReleaseReason",
            $"Domain ReleaseReason '{domainReason}' has no Avro counterpart."),
    };
}
