using Ordering.Domain.Baskets;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence.Database;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.FunctionalTests.Common;

/// <summary>
/// Hand-rolled fluent seed for the <see cref="Order"/> aggregate. Each
/// <c>Build*</c> overload walks the FSM via the aggregate's own factory and
/// transition methods so the seed produces real domain events / row-version
/// bumps — i.e., it is byte-identical to a production-emitted order.
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
        Guid buyerId,
        Guid? correlationId = null,
        Guid? paymentMethodId = null)
    {
        var order = BuildCreatedOrder(buyerId, correlationId, paymentMethodId);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Persists an order driven through the saga's happy path up to
    /// <c>Confirmed</c>. Useful for the <c>MarkOrderShipped</c> tests
    /// (Confirmed → Shipped is the only legal entry).
    /// </summary>
    public async Task<Order> CreateConfirmedOrderAsync(Guid buyerId)
    {
        var order = BuildCreatedOrder(buyerId);
        order.MarkStockReserved(Guid.CreateVersion7(), _time.GetUtcNow());
        order.MarkPaymentCompleted(Guid.CreateVersion7(), _time.GetUtcNow());
        order.Confirm(_time.GetUtcNow());

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Persists an order driven all the way through to <c>Shipped</c>.
    /// Useful for the <c>MarkOrderDelivered</c> tests and for the
    /// "cannot cancel after Shipped" 409 case.
    /// </summary>
    public async Task<Order> CreateShippedOrderAsync(Guid buyerId)
    {
        var order = BuildCreatedOrder(buyerId);
        order.MarkStockReserved(Guid.CreateVersion7(), _time.GetUtcNow());
        order.MarkPaymentCompleted(Guid.CreateVersion7(), _time.GetUtcNow());
        order.Confirm(_time.GetUtcNow());
        order.MarkShipped("DHL", "1Z999AA10123456784", _time.GetUtcNow());

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    private Order BuildCreatedOrder(
        Guid buyerId,
        Guid? correlationId = null,
        Guid? paymentMethodId = null)
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

        // Create two distinct Address instances. EF Core's owned-type
        // change-tracker treats a single shared instance attached to two
        // owners as the same entity, which trips a "property X belongs to
        // type ShippingAddress#Address but is being used with BillingAddress"
        // error during NavigationFixer. Production hits the same constraint
        // — Order.CreateFromBasket is called by the BFF with two distinct
        // Address records mapped from the basket's separate fields.
        var shipping = Address.Create("1 Test Street", null, "Prague", null, "11000", "CZ").Value;
        var billing = Address.Create("1 Test Street", null, "Prague", null, "11000", "CZ").Value;

        return Order.CreateFromBasket(
            correlationId: correlationId ?? Guid.CreateVersion7(),
            buyerId: buyerId,
            basket: basket,
            shippingAddress: shipping,
            billingAddress: billing,
            paymentMethodId: paymentMethodId ?? Guid.CreateVersion7(),
            utcNow: _time.GetUtcNow());
    }
}
