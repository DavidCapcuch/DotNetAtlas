using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Basket.UnitTests.Baskets.ValueObjects;

public class ProductSnapshotTests
{
    [Fact]
    public void Create_WhenValidInputs_RoundTripsAllFields()
    {
        // Arrange
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = Money.Create(42.50m, CurrencyCode.Eur).Value;

        // Act
        var snapshot = ProductSnapshot.Create("SKU-42", "Widget", price, captured);

        // Assert
        using (new AssertionScope())
        {
            snapshot.Sku.Should().Be("SKU-42");
            snapshot.Name.Should().Be("Widget");
            snapshot.Price.Should().Be(price);
            snapshot.CapturedAtUtc.Should().Be(captured);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-50)]
    [Trait("Category", "boundary")]
    public void Create_WhenNonPositivePrice_ThrowsDataIntegrityException(decimal nonPositive)
    {
        // Local Basket-domain invariant: ProductSnapshot.Price > 0. Mirrors Catalog.Product;
        // Money itself is sign-neutral (School B), so the rule lives on the consuming VO.

        // Arrange
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = Money.Create(nonPositive, CurrencyCode.Eur).Value;

        // Act
        var act = () => ProductSnapshot.Create("SKU-1", "Widget", price, captured);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Basket.ProductSnapshotPriceNotPositive");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "boundary")]
    public void Create_WhenSkuIsBlank_ThrowsDataIntegrityException(string? blankSku)
    {
        // Arrange
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = Money.Create(42.50m, CurrencyCode.Eur).Value;

        // Act
        var act = () => ProductSnapshot.Create(blankSku!, "Widget", price, captured);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Basket.ProductSnapshotSkuRequired");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "boundary")]
    public void Create_WhenNameIsBlank_ThrowsDataIntegrityException(string? blankName)
    {
        // Arrange
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = Money.Create(42.50m, CurrencyCode.Eur).Value;

        // Act
        var act = () => ProductSnapshot.Create("SKU-42", blankName!, price, captured);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Basket.ProductSnapshotNameRequired");
    }
}
