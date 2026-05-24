using Platform.SharedKernel.Exceptions;
using AppConfirmReservationCommand = Inventory.Application.StockItems.ConfirmReservation.ConfirmReservationCommand;
using AppReleaseReservationCommand = Inventory.Application.StockItems.ReleaseReservation.ReleaseReservationCommand;
using AppReserveStockCommand = Inventory.Application.StockItems.ReserveStock.ReserveStockCommand;
using AvroConfirmReservationCommand = Inventory.Reservations.ConfirmReservationCommand;
using AvroReleaseReason = Inventory.Reservations.ReleaseReason;
using AvroReleaseReservationCommand = Inventory.Reservations.ReleaseReservationCommand;
using AvroReserveStockCommand = Inventory.Reservations.ReserveStockCommand;
using DomainReleaseReason = Inventory.Domain.StockItems.ValueObjects.ReleaseReason;

namespace Inventory.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Translates saga-issued Avro commands on
/// <c>inventory.reservation-commands</c> to the application-layer command
/// DTOs. Pure functions, no DI, no side-effects — explicit mapping is
/// clearer than a Mapperly config here because the shape differences
/// (Avro <c>DateTime</c> with kind-defensiveness vs.
/// <c>DateTimeOffset</c>; Avro <c>ReleaseReason</c> enum vs. domain enum)
/// are all small and worth being visible. Mirrors Ordering's
/// <c>SagaCommandMappers.cs</c> pattern.
/// </summary>
internal static class SagaCommandMappers
{
    /// <summary>
    /// Maps Avro <see cref="AvroReserveStockCommand"/> to the application
    /// <see cref="AppReserveStockCommand"/>. <c>TimeToLive</c> is left null —
    /// the saga schema doesn't carry it; the application handler falls back to
    /// the service-default TTL (15 min per <c>inventory.md § 11</c>).
    /// </summary>
    // ADR-0008 — CorrelationId is passed in explicitly from the Kafka header rather than read
    // from the Avro payload field; the header is the authoritative source.
    internal static AppReserveStockCommand ToAppCommand(this AvroReserveStockCommand avro, Guid correlationId) =>
        new()
        {
            ReservationId = avro.ReservationId,
            ProductId = avro.ProductId,
            Quantity = avro.Quantity,
            OrderId = avro.OrderId,
            TimeToLive = null,
            OccurredOnUtc = ToOffset(avro.RequestedAtUtc),
            CorrelationId = correlationId,
        };

    internal static AppConfirmReservationCommand ToAppCommand(this AvroConfirmReservationCommand avro, Guid correlationId) =>
        new()
        {
            ReservationId = avro.ReservationId,
            ProductId = avro.ProductId,
            OccurredOnUtc = ToOffset(avro.RequestedAtUtc),
            CorrelationId = correlationId,
        };

    internal static AppReleaseReservationCommand ToAppCommand(this AvroReleaseReservationCommand avro, Guid correlationId) =>
        new()
        {
            ReservationId = avro.ReservationId,
            ProductId = avro.ProductId,
            Reason = MapReleaseReason(avro.ReleaseReason),
            OccurredOnUtc = ToOffset(avro.RequestedAtUtc),
            CorrelationId = correlationId,
        };

    /// <summary>
    /// Avro deserialisers produce <c>DateTimeKind.Utc</c>;
    /// <see cref="DateTime.SpecifyKind"/> is a no-op there and a defensive
    /// guard against future <c>Kind=Unspecified</c> drift. Same pattern as
    /// Ordering's <c>SagaCommandMappers.ToAppCommand</c>.
    /// </summary>
    private static DateTimeOffset ToOffset(DateTime utcDateTime) =>
        new(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), TimeSpan.Zero);

    /// <summary>
    /// Explicit mapping between the Avro enum
    /// (<see cref="AvroReleaseReason"/>) and the domain enum
    /// (<see cref="DomainReleaseReason"/>). The two share symbol names today
    /// but the explicit switch surfaces an unmapped symbol immediately
    /// (a <see cref="DataIntegrityException"/>) instead of a silent cast.
    /// </summary>
    private static DomainReleaseReason MapReleaseReason(AvroReleaseReason reason) =>
        reason switch
        {
            AvroReleaseReason.Compensation => DomainReleaseReason.Compensation,
            AvroReleaseReason.Expiry => DomainReleaseReason.Expiry,
            AvroReleaseReason.Cancellation => DomainReleaseReason.Cancellation,
            _ => throw new DataIntegrityException(
                "Inventory.UnknownReleaseReason",
                $"Avro ReleaseReason '{reason}' has no domain mapping. Schema drift between platform Avro and Inventory.Domain enums."),
        };
}
