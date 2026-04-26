using FluentValidation;

namespace Payments.Application.Transactions.CapturePayment;

internal sealed class CapturePaymentCommandValidator : AbstractValidator<CapturePaymentCommand>
{
    public CapturePaymentCommandValidator()
    {
        RuleFor(c => c.PaymentId).NotEmpty();
        RuleFor(c => c.CorrelationId).NotEmpty();
    }
}
