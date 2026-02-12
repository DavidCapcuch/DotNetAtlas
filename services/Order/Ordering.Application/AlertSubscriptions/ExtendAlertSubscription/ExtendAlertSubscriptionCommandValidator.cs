using FluentValidation;

namespace Ordering.Application.AlertSubscriptions.ExtendAlertSubscription;

public class ExtendAlertSubscriptionCommandValidator : AbstractValidator<ExtendAlertSubscriptionCommand>
{
    public ExtendAlertSubscriptionCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.PaymentMethodId)
            .NotEmpty();

        RuleFor(x => x.DurationDays)
            .GreaterThan(0);

        RuleFor(x => x.Amount)
            .GreaterThan(0);

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Currency must be a valid 3-letter ISO 4217 currency code.");
    }
}
