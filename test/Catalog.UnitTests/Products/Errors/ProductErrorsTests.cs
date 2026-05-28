using Catalog.Domain.Products.Errors;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Products.Errors;

public class ProductErrorsTests
{
    [Fact]
    public void NotFound_ReturnsNotFoundErrorWithEntityNameAndCode()
    {
        var productId = Guid.CreateVersion7();

        var error = ProductErrors.NotFound(productId);

        using (new AssertionScope())
        {
            error.Should().BeOfType<NotFoundError>();
            error.EntityName.Should().Be("Product");
            error.Id.Should().Be(productId);
            error.ErrorCode.Should().Be("Product.NotFound");
            error.Message.Should().Contain(productId.ToString());
        }
    }

    [Fact]
    public void SkuAlreadyExists_ReturnsConflictErrorWithEntityNameAndCode()
    {
        var sku = "DUP-001";

        var error = ProductErrors.SkuAlreadyExists(sku);

        using (new AssertionScope())
        {
            error.Should().BeOfType<ConflictError>();
            error.EntityName.Should().Be("Product");
            error.ErrorCode.Should().Be("Product.SkuAlreadyExists");
            error.Message.Should().Contain(sku);
        }
    }
}
