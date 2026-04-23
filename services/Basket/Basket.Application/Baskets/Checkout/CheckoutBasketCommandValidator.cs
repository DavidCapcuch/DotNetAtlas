using Basket.Application.Baskets.Common.Validators;
using FluentValidation;

namespace Basket.Application.Baskets.Checkout;

internal sealed class CheckoutBasketCommandValidator : AbstractValidator<CheckoutBasketCommand>
{
    public CheckoutBasketCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.CorrelationId).MustBeVersion7();
        RuleFor(c => c.PaymentMethodId).NotEmpty();

        RuleFor(c => c.ShippingAddress).NotNull()
            .SetValidator(new CheckoutAddressValidator());
        RuleFor(c => c.BillingAddress).NotNull()
            .SetValidator(new CheckoutAddressValidator());
    }
}
