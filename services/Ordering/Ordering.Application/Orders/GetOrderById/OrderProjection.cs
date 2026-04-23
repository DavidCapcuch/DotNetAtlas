using Ordering.Domain.Orders;

namespace Ordering.Application.Orders.GetOrderById;

/// <summary>
/// Projects an <c>Order</c> aggregate to the flat read-side DTO. Shared by
/// <see cref="GetOrderByIdQueryHandler"/> and
/// <see cref="Ordering.Application.Orders.GetOrdersByBuyer.GetOrdersByBuyerQueryHandler"/>
/// so both queries produce byte-identical projections of the same order.
/// </summary>
internal static class OrderProjection
{
    public static GetOrderByIdResponse ToResponse(Order order) =>
        new()
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            CorrelationId = order.CorrelationId,
            PaymentMethodId = order.PaymentMethodId,
            Status = order.Status.Name,
            TotalAmount = order.Total.Amount,
            Currency = order.Total.Currency.Name,
            CreatedAtUtc = order.CreatedAtUtc,
            StockReservedAtUtc = order.StockReservedAtUtc,
            PaymentCompletedAtUtc = order.PaymentCompletedAtUtc,
            ConfirmedAtUtc = order.ConfirmedAtUtc,
            DeliveredAtUtc = order.DeliveredAtUtc,
            Items = [.. order.Items.Select(i => new OrderItemDto(
                i.ProductId,
                i.ProductSnapshot.Sku,
                i.ProductSnapshot.Name,
                i.Quantity,
                i.UnitPrice.Amount,
                i.LineTotal.Amount))],
            ShippingAddress = new AddressDto(
                order.ShippingAddress.Street1,
                order.ShippingAddress.Street2,
                order.ShippingAddress.City,
                order.ShippingAddress.State,
                order.ShippingAddress.PostalCode,
                order.ShippingAddress.CountryCode),
            BillingAddress = new AddressDto(
                order.BillingAddress.Street1,
                order.BillingAddress.Street2,
                order.BillingAddress.City,
                order.BillingAddress.State,
                order.BillingAddress.PostalCode,
                order.BillingAddress.CountryCode),
            Cancellation = order.Cancellation is null
                ? null
                : new CancellationDto(
                    order.Cancellation.Reason,
                    order.Cancellation.AtStatus.Name,
                    order.Cancellation.CancelledAtUtc),
            Failure = order.Failure is null
                ? null
                : new FailureDto(
                    order.Failure.ErrorCode,
                    order.Failure.ErrorMessage,
                    order.Failure.AtStatus.Name,
                    order.Failure.FailedAtUtc),
            Shipment = order.Shipment is null
                ? null
                : new ShipmentDto(
                    order.Shipment.Carrier,
                    order.Shipment.TrackingNumber,
                    order.Shipment.ShippedAtUtc),
        };
}
