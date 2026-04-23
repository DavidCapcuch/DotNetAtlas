using FluentValidation;

namespace Ordering.Application.Orders.CreateOrder;

/// <summary>
/// User-shape validation for <see cref="CreateOrderCommand"/>. Any rule here
/// is a user-error (ValidationBehavior translates failures to a
/// <c>Result.Fail</c> before the handler runs). Deeper invariants (I-6..I-9)
/// are bug-class and enforced by <c>Order.CreateFromBasket</c> as
/// <c>DataIntegrityException</c>.
/// </summary>
public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(c => c.CorrelationId).NotEmpty();
        RuleFor(c => c.BuyerId).NotEmpty();
        RuleFor(c => c.PaymentMethodId).NotEmpty();

        RuleFor(c => c.Currency)
            .NotEmpty()
            .Matches("^[A-Z]{3}$")
            .WithMessage("Currency must be a 3-letter ISO 4217 code (uppercase).");

        RuleFor(c => c.Items)
            .NotEmpty()
            .WithMessage("Order must have at least one item.");
        RuleForEach(c => c.Items).SetValidator(new CreateOrderItemInputValidator());

        RuleFor(c => c.ShippingAddress).NotNull().SetValidator(new AddressInputValidator());
        RuleFor(c => c.BillingAddress).NotNull().SetValidator(new AddressInputValidator());
    }
}

internal sealed class CreateOrderItemInputValidator : AbstractValidator<CreateOrderItemInput>
{
    public CreateOrderItemInputValidator()
    {
        RuleFor(i => i.ProductId).NotEmpty();
        RuleFor(i => i.Sku).NotEmpty().MaximumLength(64);
        RuleFor(i => i.Name).NotEmpty().MaximumLength(200);
        RuleFor(i => i.Quantity).GreaterThan(0);
        RuleFor(i => i.UnitPriceAmount).GreaterThan(0);
    }
}

internal sealed class AddressInputValidator : AbstractValidator<AddressInput>
{
    public AddressInputValidator()
    {
        RuleFor(a => a.Street1).NotEmpty().MaximumLength(200);
        RuleFor(a => a.Street2).MaximumLength(200).When(a => a.Street2 is not null);
        RuleFor(a => a.City).NotEmpty().MaximumLength(100);
        RuleFor(a => a.State).MaximumLength(100).When(a => a.State is not null);
        RuleFor(a => a.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(a => a.CountryCode)
            .NotEmpty()
            .Matches("^[A-Z]{2}$")
            .WithMessage("CountryCode must be ISO 3166-1 alpha-2 (2 uppercase letters).");
    }
}
