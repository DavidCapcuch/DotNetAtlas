using FluentValidation.TestHelper;
using Inventory.Application.StockItems.GetStockLevelByProductId;

namespace Inventory.UnitTests.StockItems.GetStockLevelByProductId;

public sealed class GetStockLevelByProductIdQueryValidatorTests
{
    private readonly GetStockLevelByProductIdQueryValidator _validator = new();

    [Fact]
    public void Validate_EmptyProductId_Fails()
    {
        var result = _validator.TestValidate(new GetStockLevelByProductIdQuery { ProductId = Guid.Empty });

        result.ShouldHaveValidationErrorFor(q => q.ProductId);
    }

    [Fact]
    public void Validate_NonEmptyProductId_Passes()
    {
        var result = _validator.TestValidate(new GetStockLevelByProductIdQuery { ProductId = Guid.CreateVersion7() });

        result.ShouldNotHaveValidationErrorFor(q => q.ProductId);
    }
}
