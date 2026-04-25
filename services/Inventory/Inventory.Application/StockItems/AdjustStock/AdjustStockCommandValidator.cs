using FluentValidation;

namespace Inventory.Application.StockItems.AdjustStock;

public sealed class AdjustStockCommandValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Delta).NotEqual(0);
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
        RuleFor(c => c.AdjustedByUserId).NotEmpty();
    }
}
