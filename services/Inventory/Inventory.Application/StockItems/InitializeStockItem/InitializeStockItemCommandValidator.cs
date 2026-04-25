using FluentValidation;

namespace Inventory.Application.StockItems.InitializeStockItem;

public sealed class InitializeStockItemCommandValidator : AbstractValidator<InitializeStockItemCommand>
{
    public InitializeStockItemCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
    }
}
