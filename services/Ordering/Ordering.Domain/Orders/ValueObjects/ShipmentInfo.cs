using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Ordering.Domain.Orders.ValueObjects;

/// <summary>
/// Set on <see cref="Order.Shipment"/> by <c>Order.MarkShipped</c>. Carries the
/// carrier identifier and tracking number the warehouse / admin provided.
/// Free-form in v1; a SmartEnum for <see cref="Carrier"/> is deferred to v2
/// once the supported-carriers list is known (<c>ordering.md § 4.7</c>).
/// </summary>
public sealed record ShipmentInfo : ValueObject
{
    public const int MaxCarrierLength = 100;
    public const int MaxTrackingNumberLength = 100;

    public string Carrier { get; private init; } = string.Empty;
    public string TrackingNumber { get; private init; } = string.Empty;
    public DateTimeOffset ShippedAtUtc { get; private init; }

    private ShipmentInfo()
    {
    }

    public static Result<ShipmentInfo> Create(string? carrier, string? trackingNumber, DateTimeOffset shippedAtUtc)
    {
        var trimmedCarrier = carrier?.Trim() ?? string.Empty;
        if (trimmedCarrier.Length == 0)
        {
            return Result.Fail(new ValidationError(
                nameof(Carrier), "Carrier must not be empty.", "ShipmentInfo.CarrierEmpty"));
        }

        if (trimmedCarrier.Length > MaxCarrierLength)
        {
            return Result.Fail(new ValidationError(
                nameof(Carrier),
                $"Carrier must not exceed {MaxCarrierLength} characters.",
                "ShipmentInfo.CarrierTooLong"));
        }

        var trimmedTracking = trackingNumber?.Trim() ?? string.Empty;
        if (trimmedTracking.Length == 0)
        {
            return Result.Fail(new ValidationError(
                nameof(TrackingNumber),
                "Tracking number must not be empty.",
                "ShipmentInfo.TrackingNumberEmpty"));
        }

        if (trimmedTracking.Length > MaxTrackingNumberLength)
        {
            return Result.Fail(new ValidationError(
                nameof(TrackingNumber),
                $"Tracking number must not exceed {MaxTrackingNumberLength} characters.",
                "ShipmentInfo.TrackingNumberTooLong"));
        }

        return new ShipmentInfo
        {
            Carrier = trimmedCarrier,
            TrackingNumber = trimmedTracking,
            ShippedAtUtc = shippedAtUtc,
        };
    }
}
