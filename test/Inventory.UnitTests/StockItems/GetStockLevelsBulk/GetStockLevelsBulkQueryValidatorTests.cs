using FluentValidation.TestHelper;
using Inventory.Application.StockItems.GetStockLevelsBulk;

namespace Inventory.UnitTests.StockItems.GetStockLevelsBulk;

public sealed class GetStockLevelsBulkQueryValidatorTests
{
    private readonly GetStockLevelsBulkQueryValidator _validator = new();

    [Fact]
    public void Validate_SingleNonEmptyProductId_Passes()
    {
        var result = _validator.TestValidate(new GetStockLevelsBulkQuery
        {
            ProductIds = [Guid.CreateVersion7()],
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Validate_MaxProductIds_Passes()
    {
        var ids = Enumerable.Range(0, GetStockLevelsBulkQueryValidator.MaxProductIds)
            .Select(_ => Guid.CreateVersion7())
            .ToList();

        var result = _validator.TestValidate(new GetStockLevelsBulkQuery { ProductIds = ids });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Validate_EmptyList_Fails()
    {
        var result = _validator.TestValidate(new GetStockLevelsBulkQuery { ProductIds = [] });

        result.ShouldHaveValidationErrorFor(q => q.ProductIds);
    }

    [Fact]
    [Trait("Category", "boundary")]
    public void Validate_MoreThanMaxProductIds_Fails()
    {
        var ids = Enumerable.Range(0, GetStockLevelsBulkQueryValidator.MaxProductIds + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToList();

        var result = _validator.TestValidate(new GetStockLevelsBulkQuery { ProductIds = ids });

        result.ShouldHaveValidationErrorFor(q => q.ProductIds);
    }

    [Fact]
    public void Validate_ContainsEmptyProductId_Fails()
    {
        var result = _validator.TestValidate(new GetStockLevelsBulkQuery
        {
            ProductIds = [Guid.CreateVersion7(), Guid.Empty],
        });

        result.ShouldHaveValidationErrorFor("ProductIds[1]");
    }
}
