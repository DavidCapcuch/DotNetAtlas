using Catalog.Application.Products.CreateProduct;
using Catalog.Application.Products.UpdateProductPrice;

namespace Catalog.UnitTests.Products.UpdateProductPrice;

public class UpdateProductPriceCommandValidatorTests
{
    private readonly UpdateProductPriceCommandValidator _validator = new();

    [Fact]
    public void Valid_command_passes()
    {
        var cmd = new UpdateProductPriceCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewPrice = new MoneyDto { Amount = 9.99m, Currency = "USD" },
        };

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_product_id_fails()
    {
        var cmd = new UpdateProductPriceCommand
        {
            ProductId = Guid.Empty,
            NewPrice = new MoneyDto { Amount = 1m, Currency = "USD" },
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Zero_amount_fails()
    {
        var cmd = new UpdateProductPriceCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewPrice = new MoneyDto { Amount = 0m, Currency = "USD" },
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Lowercase_currency_fails()
    {
        var cmd = new UpdateProductPriceCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewPrice = new MoneyDto { Amount = 1m, Currency = "usd" },
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
