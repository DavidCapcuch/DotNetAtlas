using Ordering.Domain.Baskets;
using Ordering.Domain.Orders;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.UnitTests.Orders.Aggregates;

/// <summary>
/// Shared builders for Order aggregate tests. Keeps test bodies short and
/// focused on the invariant they exercise.
/// </summary>
internal static class OrderTestFactory
{
    public static readonly DateTimeOffset UtcNow = new(2026, 4, 22, 10, 0, 0, TimeSpan.Zero);

    public static Address ShippingAddress() =>
        Address.Create("1 Main St", null, "Prague", null, "11000", "CZ").Value;

    public static Address BillingAddress() =>
        Address.Create("2 Market St", null, "Brno", null, "60200", "CZ").Value;

    public static BasketSnapshotItem Item(Guid? productId = null, int quantity = 2, decimal unitPrice = 10m)
        => new(
            ProductId: productId ?? Guid.CreateVersion7(),
            Sku: "SKU-TEST-001",
            Name: "Test Product",
            Quantity: quantity,
            UnitPriceAmount: unitPrice);

    public static BasketSnapshot Basket(
        Guid? buyerId = null,
        CurrencyCode? currency = null,
        params BasketSnapshotItem[] items)
        => new(
            BuyerId: buyerId ?? Guid.CreateVersion7(),
            Currency: currency ?? CurrencyCode.Usd,
            Items: items.Length == 0 ? [Item()] : items);

    public static Order NewOrder() => Order.CreateFromBasket(
        orderId: Guid.CreateVersion7(),
        correlationId: Guid.CreateVersion7(),
        buyerId: Guid.CreateVersion7(),
        basket: Basket(),
        shippingAddress: ShippingAddress(),
        billingAddress: BillingAddress(),
        paymentMethodId: Guid.CreateVersion7(),
        utcNow: UtcNow);

    public static Order OrderAt(OrderStatus target)
    {
        var order = NewOrder();
        _ = order.PopDomainEvents();

        if (target == OrderStatus.Created)
        {
            return order;
        }

        order.MarkStockReserved(Guid.CreateVersion7(), UtcNow.AddMinutes(1));
        if (target == OrderStatus.StockReserved)
        {
            _ = order.PopDomainEvents();
            return order;
        }

        order.MarkPaymentCompleted(Guid.CreateVersion7(), UtcNow.AddMinutes(2));
        if (target == OrderStatus.PaymentCompleted)
        {
            _ = order.PopDomainEvents();
            return order;
        }

        order.Confirm(UtcNow.AddMinutes(3));
        if (target == OrderStatus.Confirmed)
        {
            _ = order.PopDomainEvents();
            return order;
        }

        order.MarkShipped("DHL", "TRK-42", UtcNow.AddMinutes(4));
        if (target == OrderStatus.Shipped)
        {
            _ = order.PopDomainEvents();
            return order;
        }

        order.MarkDelivered(UtcNow.AddMinutes(5));
        if (target == OrderStatus.Delivered)
        {
            _ = order.PopDomainEvents();
            return order;
        }

        throw new InvalidOperationException($"OrderAt does not support target '{target.Name}'.");
    }

    public static Order CancelledOrder()
    {
        var order = NewOrder();
        _ = order.PopDomainEvents();
        order.Cancel("buyer abandoned", UtcNow.AddMinutes(1));
        _ = order.PopDomainEvents();
        return order;
    }

    public static Order FailedOrder()
    {
        var order = NewOrder();
        _ = order.PopDomainEvents();
        order.Fail("STOCK_UNAVAILABLE", "Not enough stock", UtcNow.AddMinutes(1));
        _ = order.PopDomainEvents();
        return order;
    }
}
