using System.Collections.Immutable;
using Avro;
using Basket.Application.Baskets.Checkout;
using Basket.Domain.Baskets.Events;
using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.Application.Checkout;

/// <summary>
/// Exhaustive mapping coverage for
/// <see cref="BasketCheckoutInitiatedMapper.ToBasketCheckoutInitiatedEvent"/>.
/// Every field of the Avro contract must be verified — this is the one place where
/// downstream consumers (Checkout saga) depend on exact payload shape.
/// </summary>
public class BasketCheckoutInitiatedMapperTests
{
    [Fact]
    public void ToAvroEvent_WhenFullyPopulatedEvent_PopulatesEveryFieldCorrectly()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();

        var capturedAt = new DateTimeOffset(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);
        var occurredAt = new DateTimeOffset(2026, 04, 23, 12, 00, 00, TimeSpan.Zero);

        var snapshot = ProductSnapshot.Create("SKU-42", "Widget", Money.Create(19.9900m, CurrencyCode.Usd).Value, capturedAt);
        var item = BasketItem.BuildUnchecked(productId, snapshot, 3);
        var basketSnapshot = BasketSnapshot.Create(
            ImmutableArray.Create(item),
            BasketTotal.From(Money.Create(59.9700m, CurrencyCode.Usd).Value));

        var shipping = Address.Create("1 Main St", "Apt 2", "Springfield", "IL", "62704", "US").Value;
        var billing = Address.Create("Hlavní 10", null, "Praha", null, "11000", "CZ").Value;

        var domainEvent = new BasketCheckedOutDomainEvent
        {
            UserId = userId,
            OrderId = orderId,
            Snapshot = basketSnapshot,
            ShippingAddress = shipping,
            BillingAddress = billing,
            PaymentMethodId = paymentMethodId,
            OccurredOnUtc = occurredAt,
        };

        // Act
        var avro = domainEvent.ToBasketCheckoutInitiatedEvent();

        // Assert
        using (new AssertionScope())
        {
            avro.OrderId.Should().Be(orderId);
            avro.UserId.Should().Be(userId);
            avro.PaymentMethodId.Should().Be(paymentMethodId);
            avro.Currency.Should().Be("USD");
            avro.TotalAmount.Should().Be(new AvroDecimal(59.9700m));
            avro.InitiatedAtUtc.Should().Be(occurredAt.UtcDateTime);

            avro.Items.Should().ContainSingle();
            var line = avro.Items[0];
            line.ProductId.Should().Be(productId);
            line.Sku.Should().Be("SKU-42");
            line.Name.Should().Be("Widget");
            line.UnitPriceAmount.Should().Be(new AvroDecimal(19.9900m));
            line.UnitPriceCurrency.Should().Be("USD");
            line.Quantity.Should().Be(3);
            line.LineTotal.Should().Be(new AvroDecimal(19.9900m * 3));

            // Shipping
            avro.ShippingAddress.Street1.Should().Be("1 Main St");
            avro.ShippingAddress.Street2.Should().Be("Apt 2");
            avro.ShippingAddress.City.Should().Be("Springfield");
            avro.ShippingAddress.State.Should().Be("IL");
            avro.ShippingAddress.PostalCode.Should().Be("62704");
            avro.ShippingAddress.CountryCode.Should().Be("US");

            // Billing — null optional fields round-trip cleanly
            avro.BillingAddress.Street1.Should().Be("Hlavní 10");
            avro.BillingAddress.Street2.Should().BeNull();
            avro.BillingAddress.City.Should().Be("Praha");
            avro.BillingAddress.State.Should().BeNull();
            avro.BillingAddress.PostalCode.Should().Be("11000");
            avro.BillingAddress.CountryCode.Should().Be("CZ");
        }
    }

    [Fact]
    public void ToAvroEvent_WithMultipleItems_ComputesEachLineTotalFromSnapshotTimesQuantity()
    {
        // Arrange
        var productA = Guid.CreateVersion7();
        var productB = Guid.CreateVersion7();
        var capturedAt = new DateTimeOffset(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);

        var item1 = BasketItem.BuildUnchecked(productA, ProductSnapshot.Create("SKU-1", "N1", Money.Create(10m, CurrencyCode.Usd).Value, capturedAt), 2);
        var item2 = BasketItem.BuildUnchecked(productB, ProductSnapshot.Create("SKU-2", "N2", Money.Create(5.5m, CurrencyCode.Usd).Value, capturedAt), 4);

        var snap = BasketSnapshot.Create(
            ImmutableArray.Create(item1, item2),
            BasketTotal.From(Money.Create(20m + 22m, CurrencyCode.Usd).Value));

        var addr = Address.Create("S", null, "C", null, "P", "US").Value;
        var domainEvent = new BasketCheckedOutDomainEvent
        {
            OccurredOnUtc = capturedAt,
            UserId = Guid.CreateVersion7(),
            OrderId = Guid.CreateVersion7(),
            Snapshot = snap,
            ShippingAddress = addr,
            BillingAddress = addr,
            PaymentMethodId = Guid.CreateVersion7(),
        };

        // Act
        var avro = domainEvent.ToBasketCheckoutInitiatedEvent();

        // Assert
        using (new AssertionScope())
        {
            avro.Items.Should().HaveCount(2);
            ((decimal)avro.Items[0].LineTotal).Should().Be(20m);
            ((decimal)avro.Items[1].LineTotal).Should().Be(22.0m);
            ((decimal)avro.TotalAmount).Should().Be(42m);
        }
    }
}
