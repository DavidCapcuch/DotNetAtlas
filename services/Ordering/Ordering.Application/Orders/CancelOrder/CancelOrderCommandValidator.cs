using FluentValidation;

namespace Ordering.Application.Orders.CancelOrder;

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);

        // Buyer-initiated cancellations require a real buyer id (the
        // ownership check in the handler relies on it). The admin and saga
        // call paths deliberately set BuyerId = Guid.Empty per the
        // CancelOrderCommand docstring — guarding NotEmpty unconditionally
        // would block both the admin HTTP endpoint (M5) and the saga
        // compensation path (M4).
        RuleFor(c => c.BuyerId)
            .NotEmpty()
            .When(c => !c.IsAdmin);
    }
}
