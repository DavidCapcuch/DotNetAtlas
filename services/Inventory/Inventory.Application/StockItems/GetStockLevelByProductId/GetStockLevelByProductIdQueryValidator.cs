using FluentValidation;

namespace Inventory.Application.StockItems.GetStockLevelByProductId;

public sealed class GetStockLevelByProductIdQueryValidator : AbstractValidator<GetStockLevelByProductIdQuery>
{
    public GetStockLevelByProductIdQueryValidator()
    {
        RuleFor(q => q.ProductId).NotEmpty();
    }
}
