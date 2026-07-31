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
        // Money itself is sign-neutral, so the rule lives on the consuming VO.

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

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_WhenSkuExceedsOrderingsLimit_ThrowsDataIntegrityException()
    {
        // 65 is a literal, not MaxSkuLength + 1: an expected value derived from the constant under
        // test cannot disagree with it. Catalog's own Sku ceiling is tighter than this, so the ACL
        // cannot reach here — it is the tripwire for a Catalog that widens past what Ordering takes.

        // Arrange
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = Money.Create(42.50m, CurrencyCode.Eur).Value;

        // Act
        var act = () => ProductSnapshot.Create(new string('x', 65), "Widget", price, captured);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Basket.ProductSnapshotSkuTooLong");
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_WhenSkuIsExactlyOrderingsLimit_Succeeds()
    {
        // The other half of the boundary: Ordering rejects `> 64`, so 64 must pass. Without this,
        // a `>=` guard would reject a SKU Ordering accepts and reject it at add-item.

        // Arrange
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = Money.Create(42.50m, CurrencyCode.Eur).Value;
        var maxLengthSku = new string('x', 64);

        // Act
        var snapshot = ProductSnapshot.Create(maxLengthSku, "Widget", price, captured);

        // Assert
        snapshot.Sku.Should().Be(maxLengthSku);
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_WhenSkuIsOverLimitOnlyBeforeTrimming_StillThrows()
    {
        // Pins the raw-not-trimmed choice, which nothing else does: 65 raw, 63 after trimming.
        // Ordering measures trimmed, so "align the two" is the edit a future reader reaches for —
        // and every other length test here uses unpadded input, so all of them survive it.
        // Rejecting raw is the safe direction: it can never pass a value Ordering would refuse.

        // Arrange
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = Money.Create(42.50m, CurrencyCode.Eur).Value;

        // Act
        var act = () => ProductSnapshot.Create(new string('x', 63) + "  ", "Widget", price, captured);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Basket.ProductSnapshotSkuTooLong");
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_WhenNameExceedsOrderingsLimit_ThrowsDataIntegrityException()
    {
        // Name is the field with no headroom at all — Catalog's ProductName ceiling already equals
        // Ordering's — so any widening of Catalog's limit reaches this guard immediately.

        // Arrange
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = Money.Create(42.50m, CurrencyCode.Eur).Value;

        // Act
        var act = () => ProductSnapshot.Create("SKU-42", new string('x', 201), price, captured);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .Which.ErrorCode.Should().Be("Basket.ProductSnapshotNameTooLong");
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Create_WhenNameIsExactlyOrderingsLimit_Succeeds()
    {
        // Arrange
        var captured = new DateTimeOffset(2026, 02, 20, 12, 00, 00, TimeSpan.Zero);
        var price = Money.Create(42.50m, CurrencyCode.Eur).Value;
        var maxLengthName = new string('x', 200);

        // Act
        var snapshot = ProductSnapshot.Create("SKU-42", maxLengthName, price, captured);

        // Assert
        snapshot.Name.Should().Be(maxLengthName);
    }
}
