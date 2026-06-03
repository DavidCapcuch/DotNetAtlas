using Ordering.Domain.Orders.Events;
using Ordering.Orders;

namespace Ordering.Application.Orders.MarkOrderShipped;

internal static class OrderShippedMapper
{
    public static OrderShippedEvent ToOrderShippedEvent(this OrderShippedDomainEvent source) =>
        new()
        {
            OrderId = source.OrderId,
            BuyerId = source.BuyerId,
            Carrier = source.Carrier,
            TrackingNumber = source.TrackingNumber,
            ShippedAtUtc = source.ShippedAtUtc.UtcDateTime,
        };
}
