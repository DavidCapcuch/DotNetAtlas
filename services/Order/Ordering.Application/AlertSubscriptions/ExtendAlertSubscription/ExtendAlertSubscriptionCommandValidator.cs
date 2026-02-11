using FluentValidation;

namespace Ordering.Application.AlertSubscriptions.ExtendAlertSubscription;

public class ExtendAlertSubscriptionCommandValidator : AbstractValidator<ExtendAlertSubscriptionCommand>
{
    public ExtendAlertSubscriptionCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId cannot be empty.");

        RuleFor(x => x.PaymentMethodId)
            .NotEmpty()
            .WithMessage("PaymentMethodId is required.");

        RuleFor(x => x.DurationDays)
            .GreaterThan(0)
            .WithMessage("DurationDays must be greater than 0.");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Amount must be greater than 0.");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Currency must be a valid 3-letter ISO 4217 currency code.");

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(255)
            .WithMessage("IdempotencyKey is required and must be at most 255 characters.");
    }
}
