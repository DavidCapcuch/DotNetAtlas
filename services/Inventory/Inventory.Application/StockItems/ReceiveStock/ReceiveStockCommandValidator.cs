using FluentValidation;

namespace Inventory.Application.StockItems.ReceiveStock;

public sealed class ReceiveStockCommandValidator : AbstractValidator<ReceiveStockCommand>
{
    public ReceiveStockCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Quantity).GreaterThanOrEqualTo(1);
        RuleFor(c => c.Source).NotEmpty().MaximumLength(100);
    }
}
