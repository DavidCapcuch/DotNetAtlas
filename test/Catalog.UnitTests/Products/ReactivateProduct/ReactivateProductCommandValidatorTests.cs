using Catalog.Application.Products.ReactivateProduct;

namespace Catalog.UnitTests.Products.ReactivateProduct;

public class ReactivateProductCommandValidatorTests
{
    private readonly ReactivateProductCommandValidator _validator = new();

    [Fact]
    public void Validate_AdminFlagTrue_Passes()
    {
        // Arrange
        var cmd = new ReactivateProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            AdminReactivation = true,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AdminFlagFalse_PassesInputValidation()
    {
        // Arrange
        // The AdminReactivation business rule is enforced by the aggregate
        // (Product.Reactivate returns ProductErrors.ReactivationRequiresAdminFlag → ForbiddenError
        // → 403), not by FluentValidation pre-handler. The validator only checks input shape.
        var cmd = new ReactivateProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            AdminReactivation = false,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyProductId_Fails()
    {
        // Arrange
        var cmd = new ReactivateProductCommand
        {
            ProductId = Guid.Empty,
            AdminReactivation = true,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
