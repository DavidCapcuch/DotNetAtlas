using FluentValidation;

namespace Ordering.Application.Orders.CancelOrder;

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
        RuleFor(c => c.BuyerId).NotEmpty();
    }
}
