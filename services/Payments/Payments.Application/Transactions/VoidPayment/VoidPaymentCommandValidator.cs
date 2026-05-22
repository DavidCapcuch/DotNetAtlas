using FluentValidation;

namespace Payments.Application.Transactions.VoidPayment;

internal sealed class VoidPaymentCommandValidator : AbstractValidator<VoidPaymentCommand>
{
    public VoidPaymentCommandValidator()
    {
        RuleFor(c => c.PaymentId).NotEmpty();
        RuleFor(c => c.CorrelationId).NotEmpty();
        RuleFor(c => c.AuthorizationId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(256);
    }
}
