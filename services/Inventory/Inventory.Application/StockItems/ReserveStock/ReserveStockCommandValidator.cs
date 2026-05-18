using FluentValidation;

namespace Inventory.Application.StockItems.ReserveStock;

public sealed class ReserveStockCommandValidator : AbstractValidator<ReserveStockCommand>
{
    public ReserveStockCommandValidator()
    {
        RuleFor(c => c.ReservationId).NotEmpty();
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Quantity).GreaterThanOrEqualTo(1);
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.TimeToLive.Value)
            .InclusiveBetween(TimeSpan.FromSeconds(60), TimeSpan.FromHours(1))
            .When(c => c.TimeToLive.HasValue);
    }
}
