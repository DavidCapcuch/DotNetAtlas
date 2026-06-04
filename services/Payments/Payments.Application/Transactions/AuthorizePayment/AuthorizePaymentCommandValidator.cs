using FluentValidation;

namespace Payments.Application.Transactions.AuthorizePayment;

internal sealed class AuthorizePaymentCommandValidator : AbstractValidator<AuthorizePaymentCommand>
{
    public AuthorizePaymentCommandValidator()
    {
        RuleFor(c => c.PaymentId).NotEmpty();
        RuleFor(c => c.BuyerId).NotEmpty();
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.PaymentMethodId).NotEmpty().MaximumLength(64);
        RuleFor(c => c.IdempotencyKey).NotEmpty();
    }
}
