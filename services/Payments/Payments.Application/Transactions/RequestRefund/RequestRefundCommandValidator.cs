using FluentValidation;

namespace Payments.Application.Transactions.RequestRefund;

internal sealed class RequestRefundCommandValidator : AbstractValidator<RequestRefundCommand>
{
    public RequestRefundCommandValidator()
    {
        RuleFor(c => c.PaymentId).NotEmpty();
        RuleFor(c => c.CorrelationId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
    }
}
