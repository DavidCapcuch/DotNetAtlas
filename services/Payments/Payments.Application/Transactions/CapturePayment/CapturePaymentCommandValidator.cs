using FluentValidation;

namespace Payments.Application.Transactions.CapturePayment;

internal sealed class CapturePaymentCommandValidator : AbstractValidator<CapturePaymentCommand>
{
    public CapturePaymentCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.AuthorizationId).NotEmpty();
    }
}
