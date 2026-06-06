using FluentValidation.TestHelper;
using Inventory.Application.StockItems.GetStockLevelsBulk;

namespace Inventory.UnitTests.StockItems.GetStockLevelsBulk;

public sealed class GetStockLevelsBulkQueryValidatorTests
{
    private readonly GetStockLevelsBulkQueryValidator _validator = new();

    [Fact]
    public void SingleNonEmptyProductId_PassesValidation()
    {
        var result = _validator.TestValidate(new GetStockLevelsBulkQuery
        {
            ProductIds = [Guid.CreateVersion7()],
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void MaxProductIds_PassesValidation()
    {
        var ids = Enumerable.Range(0, GetStockLevelsBulkQueryValidator.MaxProductIds)
            .Select(_ => Guid.CreateVersion7())
            .ToList();

        var result = _validator.TestValidate(new GetStockLevelsBulkQuery { ProductIds = ids });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyList_FailsValidation()
    {
        var result = _validator.TestValidate(new GetStockLevelsBulkQuery { ProductIds = [] });

        result.ShouldHaveValidationErrorFor(q => q.ProductIds);
    }

    [Fact]
    public void MoreThanMaxProductIds_FailsValidation()
    {
        var ids = Enumerable.Range(0, GetStockLevelsBulkQueryValidator.MaxProductIds + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToList();

        var result = _validator.TestValidate(new GetStockLevelsBulkQuery { ProductIds = ids });

        result.ShouldHaveValidationErrorFor(q => q.ProductIds);
    }

    [Fact]
    public void ContainsEmptyProductId_FailsValidation()
    {
        var result = _validator.TestValidate(new GetStockLevelsBulkQuery
        {
            ProductIds = [Guid.CreateVersion7(), Guid.Empty],
        });

        result.ShouldHaveValidationErrorFor("ProductIds[1]");
    }
}
