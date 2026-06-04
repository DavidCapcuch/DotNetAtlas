using Basket.Application.Baskets.Common.Validators;
using FluentValidation;

namespace Basket.Application.Baskets.Checkout;

internal sealed class CheckoutBasketCommandValidator : AbstractValidator<CheckoutBasketCommand>
{
    public CheckoutBasketCommandValidator()
    {
        // The OrderId is allocated server-side by the command handler (Guid.CreateVersion7,
        // ADR-0029), not supplied by the caller — so there is no client-provided OrderId to
        // validate here.
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.PaymentMethodId).NotEmpty();

        RuleFor(c => c.ShippingAddress).NotNull()
            .SetValidator(new CheckoutAddressValidator());
        RuleFor(c => c.BillingAddress).NotNull()
            .SetValidator(new CheckoutAddressValidator());
    }
}
