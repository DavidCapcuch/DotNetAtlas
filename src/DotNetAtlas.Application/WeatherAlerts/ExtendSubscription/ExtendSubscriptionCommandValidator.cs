using FluentValidation;

namespace DotNetAtlas.Application.WeatherAlerts.ExtendSubscription;

public class ExtendSubscriptionCommandValidator : AbstractValidator<ExtendSubscriptionCommand>
{
    public ExtendSubscriptionCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        RuleFor(x => x.PaymentTransactionId).NotEmpty();

        RuleFor(x => x.DurationExtendedDays)
            .GreaterThan(0)
            .WithMessage("DurationDays must be greater than zero.");
    }
}
