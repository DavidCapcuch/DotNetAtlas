using Basket.Application.Baskets.Common.Errors;
using Platform.SharedKernel.Errors;

namespace Basket.UnitTests.Baskets.Application.Common.Errors;

public class BasketAclErrorsTests
{
    [Fact]
    public void CatalogUnavailable_ReturnsServiceUnavailableErrorWithResourceNameAndCode()
    {
        // Act
        var error = BasketAclErrors.CatalogUnavailable();

        // Assert
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
        // Arrange
        var productId = Guid.CreateVersion7();

        // Act
        var error = BasketAclErrors.ProductNotFound(productId);

        // Assert
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
