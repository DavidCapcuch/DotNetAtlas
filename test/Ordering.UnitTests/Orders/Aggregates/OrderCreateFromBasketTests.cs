using FluentResults.Extensions.FluentAssertions;
using Ordering.Domain.Baskets;
using Ordering.Domain.Orders;
using Ordering.Domain.Orders.Events;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.UnitTests.Orders.Aggregates;

public class OrderCreateFromBasketTests
{
    [Fact]
    public void CreateFromBasket_Valid_ReturnsCreatedOrder_RaisesOrderCreatedDomainEvent()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();
        var basket = OrderTestFactory.Basket(
            buyerId,
            CurrencyCode.Eur,
            OrderTestFactory.Item(product1, quantity: 2, unitPrice: 10m),
            OrderTestFactory.Item(product2, quantity: 3, unitPrice: 5m));
        var shipping = OrderTestFactory.ShippingAddress();
        var billing = OrderTestFactory.BillingAddress();

        // Act
        var order = Order.CreateFromBasket(
            correlationId, buyerId, basket, shipping, billing, paymentMethodId, OrderTestFactory.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            order.Id.Should().NotBe(Guid.Empty);
            order.BuyerId.Should().Be(buyerId);
            order.CorrelationId.Should().Be(correlationId);
            order.PaymentMethodId.Should().Be(paymentMethodId);
            order.Status.Should().Be(OrderStatus.Created);
            order.CreatedAtUtc.Should().Be(OrderTestFactory.UtcNow);
            order.Items.Should().HaveCount(2);
            // I-6 total = Σ line totals (2*10 + 3*5 = 35)
            order.Total.Amount.Should().Be(35m);
            order.Total.Currency.Should().Be(CurrencyCode.Eur);
            order.ShippingAddress.Should().Be(shipping);
            order.BillingAddress.Should().Be(billing);
            order.Cancellation.Should().BeNull();
            order.Failure.Should().BeNull();
            order.Shipment.Should().BeNull();
            order.StockReservationId.Should().BeNull();
            order.PaymentTransactionId.Should().BeNull();

            var evt = order.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<OrderCreatedDomainEvent>()
                .Subject;
            evt.OrderId.Should().Be(order.Id);
            evt.BuyerId.Should().Be(buyerId);
            evt.CorrelationId.Should().Be(correlationId);
            evt.Items.Should().HaveCount(2);
            evt.Total.Should().Be(order.Total);
            evt.CreatedAtUtc.Should().Be(OrderTestFactory.UtcNow);
            evt.OccurredOnUtc.Should().Be(OrderTestFactory.UtcNow);
        }
    }

    [Fact]
    public void CreateFromBasket_EmptyBasketItems_ThrowsDataIntegrityException()
    {
        // Arrange
        var basket = new BasketSnapshot(
            Guid.CreateVersion7(), CurrencyCode.Usd, Array.Empty<BasketSnapshotItem>());

        // Act
        var act = () => Order.CreateFromBasket(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            basket,
            OrderTestFactory.ShippingAddress(),
            OrderTestFactory.BillingAddress(),
            Guid.CreateVersion7(),
            OrderTestFactory.UtcNow);

        // Assert (I-7)
        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*empty basket*");
    }

    [Fact]
    public void CreateFromBasket_ItemWithZeroQuantity_ThrowsDataIntegrityException()
    {
        // Arrange
        var basket = OrderTestFactory.Basket(
            currency: CurrencyCode.Usd,
            items: OrderTestFactory.Item(quantity: 0));

        // Act
        var act = () => Order.CreateFromBasket(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            basket,
            OrderTestFactory.ShippingAddress(),
            OrderTestFactory.BillingAddress(),
            Guid.CreateVersion7(),
            OrderTestFactory.UtcNow);

        // Assert (I-8)
        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*non-positive quantity*");
    }

    [Fact]
    public void CreateFromBasket_ItemWithNegativeUnitPrice_ThrowsDataIntegrityException()
    {
        // Arrange
        var basket = OrderTestFactory.Basket(
            currency: CurrencyCode.Usd,
            items: OrderTestFactory.Item(quantity: 1, unitPrice: -5m));

        // Act
        var act = () => Order.CreateFromBasket(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            basket,
            OrderTestFactory.ShippingAddress(),
            OrderTestFactory.BillingAddress(),
            Guid.CreateVersion7(),
            OrderTestFactory.UtcNow);

        // Assert (I-8)
        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*non-positive unit price*");
    }

    [Fact]
    public void CreateFromBasket_EmptyCorrelationId_ThrowsDataIntegrityException()
    {
        // Arrange
        var basket = OrderTestFactory.Basket();

        // Act
        var act = () => Order.CreateFromBasket(
            Guid.Empty,
            Guid.CreateVersion7(),
            basket,
            OrderTestFactory.ShippingAddress(),
            OrderTestFactory.BillingAddress(),
            Guid.CreateVersion7(),
            OrderTestFactory.UtcNow);

        // Assert (I-5 bug guard)
        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*CorrelationId*");
    }

    [Fact]
    public void CreateFromBasket_EmptyBuyerId_ThrowsDataIntegrityException()
    {
        // Arrange
        var basket = OrderTestFactory.Basket();

        // Act
        var act = () => Order.CreateFromBasket(
            Guid.CreateVersion7(),
            Guid.Empty,
            basket,
            OrderTestFactory.ShippingAddress(),
            OrderTestFactory.BillingAddress(),
            Guid.CreateVersion7(),
            OrderTestFactory.UtcNow);

        // Assert (I-4 bug guard)
        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*BuyerId*");
    }

    [Fact]
    public void CreateFromBasket_NullBasketCurrency_ThrowsDataIntegrityException()
    {
        // Arrange (review H-1: symmetric null-guard alongside the GUID guards)
        var basket = new BasketSnapshot(
            BuyerId: Guid.CreateVersion7(),
            Currency: null!,
            Items: [OrderTestFactory.Item()]);

        // Act
        var act = () => Order.CreateFromBasket(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            basket,
            OrderTestFactory.ShippingAddress(),
            OrderTestFactory.BillingAddress(),
            Guid.CreateVersion7(),
            OrderTestFactory.UtcNow);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*Currency*");
    }
}
