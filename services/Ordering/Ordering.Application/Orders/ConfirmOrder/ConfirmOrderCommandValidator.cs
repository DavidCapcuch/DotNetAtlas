using FluentValidation;

namespace Ordering.Application.Orders.ConfirmOrder;

public sealed class ConfirmOrderCommandValidator : AbstractValidator<ConfirmOrderCommand>
{
    public ConfirmOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
    }
}
