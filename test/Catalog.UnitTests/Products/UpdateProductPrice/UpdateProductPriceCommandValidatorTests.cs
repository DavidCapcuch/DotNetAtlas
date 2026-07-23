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
            NewAmount = 9.99m,
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
            NewAmount = 1m,
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
            NewAmount = 0m,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
