using Catalog.Domain.Products.ValueObjects;

namespace Catalog.UnitTests.Products.ValueObjects;

public class ProductStatusTests
{
    [Fact]
    public void Draft_IsNotSellableAndNotTerminal()
    {
        // Assert
        using (new AssertionScope())
        {
            ProductStatus.Draft.IsSellable.Should().BeFalse();
            ProductStatus.Draft.IsTerminal.Should().BeFalse();
            ProductStatus.Draft.Value.Should().Be(0);
        }
    }

    [Fact]
    public void Active_IsSellableAndNotTerminal()
    {
        // Assert
        using (new AssertionScope())
        {
            ProductStatus.Active.IsSellable.Should().BeTrue();
            ProductStatus.Active.IsTerminal.Should().BeFalse();
            ProductStatus.Active.Value.Should().Be(1);
        }
    }

    [Fact]
    public void Discontinued_IsNotSellableAndNotTerminal()
    {
        // Assert
        using (new AssertionScope())
        {
            ProductStatus.Discontinued.IsSellable.Should().BeFalse();
            ProductStatus.Discontinued.IsTerminal.Should().BeFalse();
            ProductStatus.Discontinued.Value.Should().Be(2);
        }
    }

    // CAT-TST / Wave-1 closeout #200: M10 mutation-testing surfaced 6 boolean-mutation
    // survivors on CanTransitionTo. The previous Theory partially covered the matrix.
    // Below is the exhaustive 18-cell cartesian product (3 from × 3 to × 2 admin flag),
    // which kills every reachable boolean mutation in the implementation by pinning the
    // expected result at every (from, to, admin) tuple.
    [Theory]
    [InlineData(nameof(ProductStatus.Draft), nameof(ProductStatus.Draft), false, false)]
    [InlineData(nameof(ProductStatus.Draft), nameof(ProductStatus.Draft), true, false)]
    [InlineData(nameof(ProductStatus.Draft), nameof(ProductStatus.Active), false, true)]
    [InlineData(nameof(ProductStatus.Draft), nameof(ProductStatus.Active), true, true)]
    [InlineData(nameof(ProductStatus.Draft), nameof(ProductStatus.Discontinued), false, false)]
    [InlineData(nameof(ProductStatus.Draft), nameof(ProductStatus.Discontinued), true, false)]
    [InlineData(nameof(ProductStatus.Active), nameof(ProductStatus.Draft), false, false)]
    [InlineData(nameof(ProductStatus.Active), nameof(ProductStatus.Draft), true, false)]
    [InlineData(nameof(ProductStatus.Active), nameof(ProductStatus.Active), false, false)]
    [InlineData(nameof(ProductStatus.Active), nameof(ProductStatus.Active), true, false)]
    [InlineData(nameof(ProductStatus.Active), nameof(ProductStatus.Discontinued), false, true)]
    [InlineData(nameof(ProductStatus.Active), nameof(ProductStatus.Discontinued), true, true)]
    [InlineData(nameof(ProductStatus.Discontinued), nameof(ProductStatus.Draft), false, false)]
    [InlineData(nameof(ProductStatus.Discontinued), nameof(ProductStatus.Draft), true, false)]
    [InlineData(nameof(ProductStatus.Discontinued), nameof(ProductStatus.Active), false, false)]
    [InlineData(nameof(ProductStatus.Discontinued), nameof(ProductStatus.Active), true, true)]
    [InlineData(nameof(ProductStatus.Discontinued), nameof(ProductStatus.Discontinued), false, false)]
    [InlineData(nameof(ProductStatus.Discontinued), nameof(ProductStatus.Discontinued), true, false)]
    public void CanTransitionTo_MatchesTransitionMatrix(string fromName, string toName, bool adminReactivation, bool expected)
    {
        // Arrange
        var from = ProductStatus.FromName(fromName);
        var to = ProductStatus.FromName(toName);

        // Act
        var canTransition = from.CanTransitionTo(to, adminReactivation);

        // Assert
        canTransition.Should().Be(expected);
    }
}
