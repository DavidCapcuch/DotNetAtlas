using FluentValidation;

namespace Inventory.Application.StockItems.GetStockLevelsBulk;

public sealed class GetStockLevelsBulkQueryValidator : AbstractValidator<GetStockLevelsBulkQuery>
{
    /// <summary>Upper bound on a single batch — a basket-sized collection (ADR-0034 / use-cases.md § 4.4.2).</summary>
    public const int MaxProductIds = 200;

    public GetStockLevelsBulkQueryValidator()
    {
        RuleFor(q => q.ProductIds)
            .NotEmpty()
            .Must(ids => ids is null || ids.Count <= MaxProductIds)
            .WithMessage($"'{nameof(GetStockLevelsBulkQuery.ProductIds)}' must contain between 1 and {MaxProductIds} items.");

        RuleForEach(q => q.ProductIds).NotEmpty();
    }
}
