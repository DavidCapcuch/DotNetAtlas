using Catalog.Application.Products.ReactivateProduct;

namespace Catalog.UnitTests.Products.ReactivateProduct;

public class ReactivateProductCommandValidatorTests
{
    private readonly ReactivateProductCommandValidator _validator = new();

    [Fact]
    public void Admin_flag_true_passes()
    {
        var cmd = new ReactivateProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            AdminReactivation = true,
        };

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Admin_flag_false_now_passes_input_validation()
    {
        // The AdminReactivation business rule is now enforced by the aggregate
        // (Product.Reactivate returns ProductErrors.ReactivationRequiresAdminFlag → ForbiddenError
        // → 403), not by FluentValidation pre-handler. The validator only checks input shape.
        var cmd = new ReactivateProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            AdminReactivation = false,
        };

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_product_id_fails()
    {
        var cmd = new ReactivateProductCommand
        {
            ProductId = Guid.Empty,
            AdminReactivation = true,
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
