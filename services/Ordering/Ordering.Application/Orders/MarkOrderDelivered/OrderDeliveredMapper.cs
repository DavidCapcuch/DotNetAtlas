using Ordering.Domain.Orders.Events;
using Ordering.Orders;

namespace Ordering.Application.Orders.MarkOrderDelivered;

internal static class OrderDeliveredMapper
{
    public static OrderDeliveredEvent ToOrderDeliveredEvent(this OrderDeliveredDomainEvent source) =>
        new()
        {
            OrderId = source.OrderId,
            BuyerId = source.BuyerId,
            DeliveredAtUtc = source.DeliveredAtUtc.UtcDateTime,
        };
}
