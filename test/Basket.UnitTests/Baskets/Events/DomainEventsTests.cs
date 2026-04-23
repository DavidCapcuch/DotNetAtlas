using Basket.Domain.Baskets.Events;
using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.Events;

/// <summary>
/// Shape-level assertions for the seven internal domain events. Each event must:
/// inherit <see cref="DomainEvent"/>, be a sealed record, and require its caller
/// to supply the documented fields. The aggregate tests elsewhere assert the
/// events are raised at the correct moments.
/// </summary>
public class DomainEventsTests
{
    [Fact]
    public void BasketCreated_IsSealedRecordAndInheritsDomainEvent()
    {
        var e = new BasketCreatedDomainEvent { UserId = Guid.CreateVersion7() };

        using (new AssertionScope())
        {
            e.Should().BeAssignableTo<DomainEvent>();
            typeof(BasketCreatedDomainEvent).IsSealed.Should().BeTrue();
            e.OccurredOnUtc.Should().NotBe(default);
        }
    }

    [Fact]
    public void ItemAddedToBasket_CarriesCapturedPrice()
    {
        var price = new Money(10m, CurrencyCode.Usd);

        var e = new ItemAddedToBasketDomainEvent
        {
            UserId = Guid.CreateVersion7(),
            ProductId = Guid.CreateVersion7(),
            Quantity = 2,
            CapturedPrice = price,
        };

        using (new AssertionScope())
        {
            e.Should().BeAssignableTo<DomainEvent>();
            e.CapturedPrice.Should().Be(price);
            e.Quantity.Should().Be(2);
        }
    }

    [Fact]
    public void ItemRemovedFromBasket_CarriesIds()
    {
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();

        var e = new ItemRemovedFromBasketDomainEvent { UserId = userId, ProductId = productId };

        using (new AssertionScope())
        {
            e.Should().BeAssignableTo<DomainEvent>();
            e.UserId.Should().Be(userId);
            e.ProductId.Should().Be(productId);
        }
    }

    [Fact]
    public void ItemQuantityChanged_CarriesOldAndNewQuantities()
    {
        var e = new ItemQuantityChangedDomainEvent
        {
            UserId = Guid.CreateVersion7(),
            ProductId = Guid.CreateVersion7(),
            OldQuantity = 1,
            NewQuantity = 4,
        };

        using (new AssertionScope())
        {
            e.Should().BeAssignableTo<DomainEvent>();
            e.OldQuantity.Should().Be(1);
            e.NewQuantity.Should().Be(4);
        }
    }

    [Fact]
    public void BasketPricesRefreshed_ListsChanges()
    {
        var productId = Guid.CreateVersion7();
        var change = new PriceChange(
            productId,
            new Money(10m, CurrencyCode.Usd),
            new Money(12m, CurrencyCode.Usd));

        var e = new BasketPricesRefreshedDomainEvent
        {
            UserId = Guid.CreateVersion7(),
            Changes = [change],
        };

        using (new AssertionScope())
        {
            e.Should().BeAssignableTo<DomainEvent>();
            e.Changes.Should().ContainSingle().Which.ProductId.Should().Be(productId);
        }
    }

    [Fact]
    public void BasketCleared_IsSealedRecord()
    {
        var e = new BasketClearedDomainEvent { UserId = Guid.CreateVersion7() };

        using (new AssertionScope())
        {
            e.Should().BeAssignableTo<DomainEvent>();
            typeof(BasketClearedDomainEvent).IsSealed.Should().BeTrue();
        }
    }

    [Fact]
    public void BasketCheckedOut_CarriesCorrelationIdAndSnapshot()
    {
        var correlationId = Guid.CreateVersion7();
        var item = new BasketItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1);
        var snapshot = new BasketSnapshot([item], new BasketTotal(new Money(10m, CurrencyCode.Usd)));
        var shipping = BasketTestData.Address("US");
        var billing = BasketTestData.Address("US");
        var paymentMethodId = Guid.CreateVersion7();

        var e = new BasketCheckedOutDomainEvent
        {
            UserId = Guid.CreateVersion7(),
            CorrelationId = correlationId,
            Snapshot = snapshot,
            ShippingAddress = shipping,
            BillingAddress = billing,
            PaymentMethodId = paymentMethodId,
        };

        using (new AssertionScope())
        {
            e.Should().BeAssignableTo<DomainEvent>();
            e.CorrelationId.Should().Be(correlationId);
            e.Snapshot.Should().Be(snapshot);
            e.ShippingAddress.Should().Be(shipping);
            e.BillingAddress.Should().Be(billing);
            e.PaymentMethodId.Should().Be(paymentMethodId);
        }
    }
}
