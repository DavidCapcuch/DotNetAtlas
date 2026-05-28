using Basket.Application.Baskets.Common.Errors;
using Platform.SharedKernel.Errors;

namespace Basket.UnitTests.Baskets.Application.Common.Errors;

public class BasketAclErrorsTests
{
    [Fact]
    public void CatalogUnavailable_ReturnsServiceUnavailableErrorWithResourceNameAndCode()
    {
        var error = BasketAclErrors.CatalogUnavailable();

        using (new AssertionScope())
        {
            error.Should().BeOfType<ServiceUnavailableError>();
            error.ResourceName.Should().Be("Catalog");
            error.ErrorCode.Should().Be("Basket.CatalogUnavailable");
            error.Message.Should().Contain("temporarily unavailable");
        }
    }

    [Fact]
    public void ProductNotFound_ReturnsNotFoundErrorWithEntityNameAndCode()
    {
        var productId = Guid.CreateVersion7();

        var error = BasketAclErrors.ProductNotFound(productId);

        using (new AssertionScope())
        {
            error.Should().BeOfType<NotFoundError>();
            error.EntityName.Should().Be("Product");
            error.Id.Should().Be(productId);
            error.ErrorCode.Should().Be("Basket.ProductNotFound");
            error.Message.Should().Contain(productId.ToString());
        }
    }
}
