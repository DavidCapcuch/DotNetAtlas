using Catalog.Application.Products.CreateProduct;
using Catalog.Application.Products.UpdateProductPrice;

namespace Catalog.UnitTests.Products.UpdateProductPrice;

public class UpdateProductPriceCommandValidatorTests
{
    private readonly UpdateProductPriceCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        // Arrange
        var cmd = new UpdateProductPriceCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewPrice = new MoneyDto { Amount = 9.99m, Currency = "USD" },
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyProductId_Fails()
    {
        // Arrange
        var cmd = new UpdateProductPriceCommand
        {
            ProductId = Guid.Empty,
            NewPrice = new MoneyDto { Amount = 1m, Currency = "USD" },
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ZeroAmount_Fails()
    {
        // Arrange
        var cmd = new UpdateProductPriceCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewPrice = new MoneyDto { Amount = 0m, Currency = "USD" },
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_LowercaseCurrency_Fails()
    {
        // Arrange
        var cmd = new UpdateProductPriceCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewPrice = new MoneyDto { Amount = 1m, Currency = "usd" },
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
