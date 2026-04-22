using FluentResults.Extensions.FluentAssertions;
using Ordering.Domain.Orders.ValueObjects;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.UnitTests.Orders.ValueObjects;

public class OrderItemTests
{
    private static readonly ProductSnapshot Snapshot =
        ProductSnapshot.Create("SKU-001", "Widget").Value;

    [Fact]
    public void Create_Valid_ComputesLineTotalAsUnitPriceTimesQuantity()
    {
        var productId = Guid.CreateVersion7();
        var unitPrice = Money.Create(12.50m, CurrencyCode.Usd).Value;

        var result = OrderItem.Create(productId, Snapshot, quantity: 4, unitPrice);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.ProductId.Should().Be(productId);
            result.Value.Quantity.Should().Be(4);
            result.Value.UnitPrice.Should().Be(unitPrice);
            result.Value.LineTotal.Amount.Should().Be(50m);
            result.Value.LineTotal.Currency.Should().Be(CurrencyCode.Usd);
        }
    }

    [Fact]
    public void Create_EmptyProductId_ReturnsProductIdEmptyError()
    {
        var unitPrice = Money.Create(1m, CurrencyCode.Usd).Value;

        var result = OrderItem.Create(Guid.Empty, Snapshot, quantity: 1, unitPrice);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "OrderItem.ProductIdEmpty");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Create_NonPositiveQuantity_ReturnsQuantityNotPositiveError(int quantity)
    {
        var unitPrice = Money.Create(1m, CurrencyCode.Usd).Value;

        var result = OrderItem.Create(Guid.CreateVersion7(), Snapshot, quantity, unitPrice);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "OrderItem.QuantityNotPositive");
        }
    }

    [Fact]
    public void Create_NonPositiveUnitPriceViaDirectConstruction_ReturnsUnitPriceNotPositiveError()
    {
        // Defense-in-depth: Money.Create blocks non-positive amounts upstream, but
        // a future caller could build a Money record directly (e.g. EF Core
        // materialization for a corrupted row). The VO must still reject.
        var nonPositive = new Money(-1m, CurrencyCode.Usd);

        var result = OrderItem.Create(Guid.CreateVersion7(), Snapshot, quantity: 1, nonPositive);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "OrderItem.UnitPriceNotPositive");
        }
    }

    [Fact]
    public void Create_NullSnapshot_ThrowsArgumentNullException()
    {
        var unitPrice = Money.Create(1m, CurrencyCode.Usd).Value;

        var act = () => OrderItem.Create(Guid.CreateVersion7(), productSnapshot: null!, quantity: 1, unitPrice);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_NullUnitPrice_ThrowsArgumentNullException()
    {
        var act = () => OrderItem.Create(Guid.CreateVersion7(), Snapshot, quantity: 1, unitPrice: null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
