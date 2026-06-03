using Ordering.Domain.Baskets;
using Ordering.Domain.Orders;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.UnitTests.Application.Common;

/// <summary>
/// Aggregate-level test helpers for Application-layer tests. Parallels
/// <c>Ordering.UnitTests.Orders.Aggregates.OrderTestFactory</c> but is
/// positioned under <c>Application/Common</c> so Application-tier tests are
/// self-contained.
/// </summary>
public static class TestAggregate
{
    public static readonly DateTimeOffset UtcNow = new(2026, 4, 22, 10, 0, 0, TimeSpan.Zero);

    public static Address ShippingAddress() =>
        Address.Create("1 Main St", null, "Prague", null, "11000", "CZ").Value;

    public static Address BillingAddress() =>
        Address.Create("2 Market St", null, "Brno", null, "60200", "CZ").Value;

    public static BasketSnapshotItem Item() =>
        new(
            ProductId: Guid.CreateVersion7(),
            Sku: "SKU-1",
            Name: "Test Product",
            Quantity: 2,
            UnitPriceAmount: 10m);

    public static BasketSnapshot Basket(Guid? buyerId = null) =>
        new(
            BuyerId: buyerId ?? Guid.CreateVersion7(),
            Currency: CurrencyCode.Usd,
            Items: [Item()]);

    public static Order NewOrder(Guid? correlationId = null, Guid? buyerId = null) =>
        Order.CreateFromBasket(
            orderId: Guid.CreateVersion7(),
            correlationId: correlationId ?? Guid.CreateVersion7(),
            buyerId: buyerId ?? Guid.CreateVersion7(),
            basket: Basket(buyerId),
            shippingAddress: ShippingAddress(),
            billingAddress: BillingAddress(),
            paymentMethodId: Guid.CreateVersion7(),
            utcNow: UtcNow);

    public static Order OrderAt(OrderStatus status, Guid? buyerId = null)
    {
        var order = NewOrder(buyerId: buyerId);
        _ = order.PopDomainEvents();

        if (status == OrderStatus.Created)
        {
            return order;
        }

        order.MarkStockReserved(Guid.CreateVersion7(), UtcNow.AddMinutes(1));
        _ = order.PopDomainEvents();
        if (status == OrderStatus.StockReserved)
        {
            return order;
        }

        order.MarkPaymentCompleted(Guid.CreateVersion7(), UtcNow.AddMinutes(2));
        _ = order.PopDomainEvents();
        if (status == OrderStatus.PaymentCompleted)
        {
            return order;
        }

        order.Confirm(UtcNow.AddMinutes(3));
        _ = order.PopDomainEvents();
        if (status == OrderStatus.Confirmed)
        {
            return order;
        }

        order.MarkShipped("DHL", "TRK-42", UtcNow.AddMinutes(4));
        _ = order.PopDomainEvents();
        if (status == OrderStatus.Shipped)
        {
            return order;
        }

        order.MarkDelivered(UtcNow.AddMinutes(5));
        _ = order.PopDomainEvents();
        if (status == OrderStatus.Delivered)
        {
            return order;
        }

        throw new InvalidOperationException($"TestAggregate.OrderAt does not support '{status.Name}'.");
    }
}
