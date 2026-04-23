using Basket.Application.Baskets.Common.Contracts;
using FluentValidation;

namespace Basket.Application.Baskets.Common.Validators;

/// <summary>
/// Shared <see cref="CheckoutAddressDto"/> validator — used by
/// <c>CheckoutBasketCommandValidator</c> for both shipping and billing addresses.
/// Enforces the "basic shape" policy from <c>use-cases.md § 2.1.6</c>:
/// required string fields non-empty + ISO 3166-1 alpha-2 country code.
/// </summary>
internal sealed class CheckoutAddressValidator : AbstractValidator<CheckoutAddressDto>
{
    public CheckoutAddressValidator()
    {
        // Length ceilings mirror Platform.SharedKernel.ValueObjects.Address.Create — the
        // handler's Address.Create call is the second enforcement point and must never
        // reject something the validator accepted. See M4 pre-commit review H1.
        RuleFor(x => x.Street1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Street2).MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.CountryCode)
            .NotEmpty()
            .Length(2)
            .Matches("^[A-Z]{2}$")
            .WithMessage("CountryCode must be ISO 3166-1 alpha-2 (2 uppercase letters).");
    }
}
