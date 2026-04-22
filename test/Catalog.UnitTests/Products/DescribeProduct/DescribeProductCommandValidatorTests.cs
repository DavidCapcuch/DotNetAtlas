using Catalog.Application.Products.DescribeProduct;

namespace Catalog.UnitTests.Products.DescribeProduct;

public class DescribeProductCommandValidatorTests
{
    private readonly DescribeProductCommandValidator _validator = new();

    [Fact]
    public void Valid_description_passes()
    {
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = "plain text description",
        };

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_product_id_fails()
    {
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.Empty,
            NewDescription = "desc",
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Html_description_fails()
    {
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = "has <b>bold</b>",
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_description_passes()
    {
        // Per use-cases spec: 0-4000 chars allowed; empty string clears the description.
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = string.Empty,
        };

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }
}
