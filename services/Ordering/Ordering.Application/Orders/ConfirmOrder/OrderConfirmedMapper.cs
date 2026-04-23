using Ordering.Domain.Orders.Events;
using Ordering.Orders;

namespace Ordering.Application.Orders.ConfirmOrder;

/// <summary>
/// Maps <see cref="OrderConfirmedDomainEvent"/> → Avro
/// <see cref="OrderConfirmedEvent"/>. Manual because Mapperly requires a
/// user mapping anyway for <c>DateTimeOffset → DateTime</c> and the
/// transformation is trivial.
/// </summary>
internal static class OrderConfirmedMapper
{
    public static OrderConfirmedEvent ToOrderConfirmedEvent(this OrderConfirmedDomainEvent source) =>
        new()
        {
            OrderId = source.OrderId,
            CorrelationId = source.CorrelationId,
            BuyerId = source.BuyerId,
            ConfirmedAtUtc = source.OccurredOnUtc.UtcDateTime,
        };
}
