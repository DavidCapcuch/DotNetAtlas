using Basket.Domain.Baskets.Errors;

namespace Basket.UnitTests.Baskets.Errors;

public class BasketErrorsTests
{
    [Fact]
    public void EmptyBasket_HasExpectedShape()
    {
        var err = BasketErrors.EmptyBasket();

        using (new AssertionScope())
        {
            err.PropertyName.Should().Be("Basket");
            err.ErrorCode.Should().Be("Basket.Empty");
            err.Message.Should().Contain("at least one item");
        }
    }

    [Fact]
    public void MaxItemsReached_EmbedsMaxInMessageAndCarriesErrorCode()
    {
        var err = BasketErrors.MaxItemsReached(max: 50);

        using (new AssertionScope())
        {
            err.PropertyName.Should().Be("Items");
            err.ErrorCode.Should().Be("Basket.MaxItemsReached");
            err.Message.Should().Contain("50");
        }
    }

    [Fact]
    public void InvalidQuantity_HasExpectedShape()
    {
        var err = BasketErrors.InvalidQuantity();

        using (new AssertionScope())
        {
            err.PropertyName.Should().Be("Quantity");
            err.ErrorCode.Should().Be("Basket.InvalidQuantity");
        }
    }

    [Fact]
    public void CurrencyMismatch_HasExpectedShape()
    {
        var err = BasketErrors.CurrencyMismatch();

        using (new AssertionScope())
        {
            err.PropertyName.Should().Be("Currency");
            err.ErrorCode.Should().Be("Basket.CurrencyMismatch");
        }
    }

    [Fact]
    public void CatalogUnavailable_HasExpectedShape()
    {
        var err = BasketErrors.CatalogUnavailable();

        using (new AssertionScope())
        {
            err.PropertyName.Should().Be("Catalog");
            err.ErrorCode.Should().Be("Basket.CatalogUnavailable");
        }
    }

    [Fact]
    public void ProductNotFound_EmbedsProductIdInMessage()
    {
        var productId = Guid.CreateVersion7();

        var err = BasketErrors.ProductNotFound(productId);

        using (new AssertionScope())
        {
            err.PropertyName.Should().Be("ProductId");
            err.ErrorCode.Should().Be("Basket.ProductNotFound");
            err.Message.Should().Contain(productId.ToString());
        }
    }

    [Fact]
    public void ItemNotFound_EmbedsProductIdInMessage()
    {
        var productId = Guid.CreateVersion7();

        var err = BasketErrors.ItemNotFound(productId);

        using (new AssertionScope())
        {
            err.PropertyName.Should().Be("ProductId");
            err.ErrorCode.Should().Be("Basket.ItemNotFound");
            err.Message.Should().Contain(productId.ToString());
        }
    }

    [Fact]
    public void BasketItemErrors_InvalidQuantity_HasExpectedShape()
    {
        var err = BasketItemErrors.InvalidQuantity();

        using (new AssertionScope())
        {
            err.PropertyName.Should().Be("Quantity");
            err.ErrorCode.Should().Be("BasketItem.InvalidQuantity");
        }
    }
}
