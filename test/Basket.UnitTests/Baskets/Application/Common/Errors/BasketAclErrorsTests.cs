using Basket.Application.Baskets.Common.Errors;

namespace Basket.UnitTests.Baskets.Application.Common.Errors;

public class BasketAclErrorsTests
{
    [Fact]
    public void CatalogUnavailable_HasExpectedShape()
    {
        var err = BasketAclErrors.CatalogUnavailable();

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

        var err = BasketAclErrors.ProductNotFound(productId);

        using (new AssertionScope())
        {
            err.PropertyName.Should().Be("ProductId");
            err.ErrorCode.Should().Be("Basket.ProductNotFound");
            err.Message.Should().Contain(productId.ToString());
        }
    }
}
