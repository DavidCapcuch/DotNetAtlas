using FluentValidation;

namespace Ordering.Application.Orders.MarkOrderPaymentCompleted;

public sealed class MarkOrderPaymentCompletedCommandValidator : AbstractValidator<MarkOrderPaymentCompletedCommand>
{
    public MarkOrderPaymentCompletedCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.PaymentTransactionId).NotEmpty();
    }
}
