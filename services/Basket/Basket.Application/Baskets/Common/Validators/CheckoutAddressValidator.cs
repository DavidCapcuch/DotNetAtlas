using Basket.Application.Baskets.Common.Contracts;
using FluentValidation;
using Platform.SharedKernel.ValueObjects;

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
        // Length ceilings come from Platform.SharedKernel.ValueObjects.Address — the
        // handler's Address.Create call is the second enforcement point and must never
        // reject something the validator accepted.
        RuleFor(x => x.Street1).NotEmpty().MaximumLength(Address.Street1MaxLength);
        RuleFor(x => x.Street2).MaximumLength(Address.Street2MaxLength);
        RuleFor(x => x.City).NotEmpty().MaximumLength(Address.CityMaxLength);
        RuleFor(x => x.State).MaximumLength(Address.StateMaxLength);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(Address.PostalCodeMaxLength);
        RuleFor(x => x.CountryCode)
            .NotEmpty()
            .Length(Address.CountryCodeLength)
            .Matches("^[A-Z]{2}$")
            .WithMessage("CountryCode must be ISO 3166-1 alpha-2 (2 uppercase letters).");
    }
}
