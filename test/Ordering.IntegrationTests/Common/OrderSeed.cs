using Ordering.Domain.Baskets;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence.Database;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.IntegrationTests.Common;

/// <summary>
/// Hand-rolled seed for the <see cref="Order"/> aggregate. Each <c>Create*Async</c> method walks
/// the FSM via the aggregate's own factory and transitions, so seeded rows carry real domain events
/// and row-version bumps. Tests use it to set up the source state a scenario starts from, at either
/// entrance.
/// </summary>
internal sealed class OrderSeed
{
    private readonly OrderingDbContext _db;
    private readonly TimeProvider _time;

    public OrderSeed(OrderingDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>
    /// Persists a <c>Created</c>-status order with one item.
    /// </summary>
    public async Task<Order> CreateOrderAsync(
        Guid? buyerId = null,
        CancellationToken cancellationToken = default)
    {
        var order = BuildCreatedOrder(buyerId ?? Guid.CreateVersion7());
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    /// <summary>
    /// Persists an order driven through the saga's happy path up to
    /// <c>Confirmed</c>. Useful for the cancel-from-Confirmed case
    /// (Session 2 Example 2) and as the source-state for shipping flows.
    /// </summary>
    public async Task<Order> CreateConfirmedOrderAsync(
        Guid? buyerId = null,
        CancellationToken cancellationToken = default)
    {
        var order = BuildCreatedOrder(buyerId ?? Guid.CreateVersion7());
        order.MarkStockReserved(Guid.CreateVersion7(), _time.GetUtcNow());
        order.MarkPaymentCompleted(Guid.CreateVersion7(), _time.GetUtcNow());
        order.Confirm(_time.GetUtcNow());

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    /// <summary>
    /// Persists an order driven all the way through to <c>Shipped</c>.
    /// Used by Session 1 Example 4 (backwards-walk Confirm-on-Shipped) and
    /// Session 2 Example 3 (cancel-from-Shipped).
    /// </summary>
    public async Task<Order> CreateShippedOrderAsync(
        Guid? buyerId = null,
        CancellationToken cancellationToken = default)
    {
        var order = BuildCreatedOrder(buyerId ?? Guid.CreateVersion7());
        order.MarkStockReserved(Guid.CreateVersion7(), _time.GetUtcNow());
        order.MarkPaymentCompleted(Guid.CreateVersion7(), _time.GetUtcNow());
        order.Confirm(_time.GetUtcNow());
        order.MarkShipped("DHL", "1Z999AA10123456784", _time.GetUtcNow());

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    /// <summary>
    /// Persists an order driven all the way through to <c>Delivered</c>.
    /// Used by Session 2 Example 4 (cancel-from-Delivered).
    /// </summary>
    public async Task<Order> CreateDeliveredOrderAsync(
        Guid? buyerId = null,
        CancellationToken cancellationToken = default)
    {
        var order = BuildCreatedOrder(buyerId ?? Guid.CreateVersion7());
        order.MarkStockReserved(Guid.CreateVersion7(), _time.GetUtcNow());
        order.MarkPaymentCompleted(Guid.CreateVersion7(), _time.GetUtcNow());
        order.Confirm(_time.GetUtcNow());
        order.MarkShipped("DHL", "1Z999AA10123456784", _time.GetUtcNow());
        order.MarkDelivered(_time.GetUtcNow());

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    private Order BuildCreatedOrder(Guid buyerId)
    {
        var basket = new BasketSnapshot(
            BuyerId: buyerId,
            Currency: CurrencyCode.Eur,
            Items:
            [
                new BasketSnapshotItem(
                    ProductId: Guid.CreateVersion7(),
                    Sku: "SKU-001",
                    Name: "Test Widget",
                    Quantity: 2,
                    UnitPriceAmount: 9.99m),
            ]);

        var shipping = Address.Create("1 Test Street", null, "Prague", null, "11000", "CZ").Value;
        var billing = Address.Create("2 Billing Road", null, "Brno", null, "60200", "CZ").Value;

        return Order.CreateFromBasket(
            orderId: Guid.CreateVersion7(),
            buyerId: buyerId,
            basket: basket,
            shippingAddress: shipping,
            billingAddress: billing,
            paymentMethodId: Guid.CreateVersion7(),
            utcNow: _time.GetUtcNow());
    }
}
