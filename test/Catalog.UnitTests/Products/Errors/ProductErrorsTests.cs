using Catalog.Domain.Products.Errors;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Products.Errors;

public class ProductErrorsTests
{
    [Fact]
    public void NotFound_PopulatesPropertyNameMessageAndCode()
    {
        var productId = Guid.CreateVersion7();

        var error = ProductErrors.NotFound(productId);

        using (new AssertionScope())
        {
            error.Should().BeOfType<ValidationError>();
            error.PropertyName.Should().Be("ProductId");
            error.ErrorCode.Should().Be("Product.NotFound");
            error.Message.Should().Contain(productId.ToString());
        }
    }

    [Fact]
    public void SkuAlreadyExists_PopulatesPropertyNameMessageAndCode()
    {
        var sku = "DUP-001";

        var error = ProductErrors.SkuAlreadyExists(sku);

        using (new AssertionScope())
        {
            error.Should().BeOfType<ValidationError>();
            error.PropertyName.Should().Be("Sku");
            error.ErrorCode.Should().Be("Product.SkuAlreadyExists");
            error.Message.Should().Contain(sku);
        }
    }
}
