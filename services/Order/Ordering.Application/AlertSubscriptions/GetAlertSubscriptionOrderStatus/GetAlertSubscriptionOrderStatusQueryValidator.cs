using FluentValidation;

namespace Ordering.Application.AlertSubscriptions.GetAlertSubscriptionOrderStatus;

public class GetAlertSubscriptionOrderStatusQueryValidator : AbstractValidator<GetAlertSubscriptionOrderStatusQuery>
{
    public GetAlertSubscriptionOrderStatusQueryValidator()
    {
        RuleFor(gfr => gfr.Id)
            .NotEmpty()
            .WithMessage("Feedback ID must not be empty.");
    }
}
