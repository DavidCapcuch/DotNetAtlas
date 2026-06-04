using Ordering.Domain.Orders;
using Ordering.Domain.Orders.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.Application.Orders.GetOrderById;

/// <summary>
/// Read-side projection of an <c>Order</c>. Flat DTO — deliberately avoids
/// exposing the aggregate's mutation surface to the API layer.
/// </summary>
public sealed class GetOrderByIdResponse
{
    public required Guid OrderId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid PaymentMethodId { get; init; }

    public required string Status { get; init; }

    public required decimal TotalAmount { get; init; }

    public required string Currency { get; init; }

    public required IReadOnlyList<OrderItemDto> Items { get; init; }

    public required AddressDto ShippingAddress { get; init; }

    public required AddressDto BillingAddress { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? StockReservedAtUtc { get; init; }

    public DateTimeOffset? PaymentCompletedAtUtc { get; init; }

    public DateTimeOffset? ConfirmedAtUtc { get; init; }

    public DateTimeOffset? DeliveredAtUtc { get; init; }

    public CancellationDto? Cancellation { get; init; }

    public FailureDto? Failure { get; init; }

    public ShipmentDto? Shipment { get; init; }
}

public sealed record OrderItemDto(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    decimal UnitPriceAmount,
    decimal LineTotalAmount);

public sealed record AddressDto(
    string Street1,
    string? Street2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);

public sealed record CancellationDto(string Reason, string AtStatus, DateTimeOffset CancelledAtUtc);

public sealed record FailureDto(string ErrorCode, string ErrorMessage, string AtStatus, DateTimeOffset FailedAtUtc);

public sealed record ShipmentDto(string Carrier, string TrackingNumber, DateTimeOffset ShippedAtUtc);
