using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.ValueObjects;

public class ProductSnapshotTests
{
    [Fact]
    public void Create_RoundTripsAllFields()
    {
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = Money.Create(42.50m, CurrencyCode.Eur).Value;

        var snapshot = ProductSnapshot.Create("SKU-42", "Widget", price, captured);

        using (new AssertionScope())
        {
            snapshot.Sku.Should().Be("SKU-42");
            snapshot.Name.Should().Be("Widget");
            snapshot.Price.Should().Be(price);
            snapshot.CapturedAtUtc.Should().Be(captured);
        }
    }

    [Fact]
    public void StructuralEquality_MatchingValues_AreEqual()
    {
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);

        var a = ProductSnapshot.Create("SKU-A", "Name", Money.Create(10m, CurrencyCode.Usd).Value, captured);
        var b = ProductSnapshot.Create("SKU-A", "Name", Money.Create(10m, CurrencyCode.Usd).Value, captured);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void StructuralEquality_DifferentPrice_AreNotEqual()
    {
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);

        var a = ProductSnapshot.Create("SKU-A", "Name", Money.Create(10m, CurrencyCode.Usd).Value, captured);
        var b = ProductSnapshot.Create("SKU-A", "Name", Money.Create(11m, CurrencyCode.Usd).Value, captured);

        a.Should().NotBe(b);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-50)]
    public void Create_NonPositivePrice_ThrowsDataIntegrityException(decimal nonPositive)
    {
        // Local Basket-domain invariant: ProductSnapshot.Price > 0. Mirrors Catalog.Product;
        // Money itself is sign-neutral (School B), so the rule lives on the consuming VO.
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = Money.Create(nonPositive, CurrencyCode.Eur).Value;

        var act = () => ProductSnapshot.Create("SKU-1", "Widget", price, captured);

        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Basket.ProductSnapshotPriceNotPositive");
    }
}
