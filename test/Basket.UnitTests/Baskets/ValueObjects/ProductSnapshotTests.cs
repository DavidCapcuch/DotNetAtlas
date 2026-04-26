using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.ValueObjects;

public class ProductSnapshotTests
{
    [Fact]
    public void Create_RoundTripsAllFields()
    {
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = new Money(42.50m, CurrencyCode.Eur);

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

        var a = ProductSnapshot.Create("SKU-A", "Name", new Money(10m, CurrencyCode.Usd), captured);
        var b = ProductSnapshot.Create("SKU-A", "Name", new Money(10m, CurrencyCode.Usd), captured);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void StructuralEquality_DifferentPrice_AreNotEqual()
    {
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);

        var a = ProductSnapshot.Create("SKU-A", "Name", new Money(10m, CurrencyCode.Usd), captured);
        var b = ProductSnapshot.Create("SKU-A", "Name", new Money(11m, CurrencyCode.Usd), captured);

        a.Should().NotBe(b);
    }
}
