using FluentValidation;

namespace Weather.Application.WeatherAlerts.PurchaseSubscription;

public class PurchaseSubscriptionCommandValidator : AbstractValidator<PurchaseSubscriptionCommand>
{
    public PurchaseSubscriptionCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        RuleFor(x => x.PaymentTransactionId).NotEmpty();

        RuleFor(x => x.DurationDays)
            .GreaterThan(0)
            .WithMessage("DurationDays must be greater than zero.");
    }
}
