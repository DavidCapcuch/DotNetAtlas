using FluentValidation;
using Weather.Application.WeatherFeedback.Common.Validation;

namespace Weather.Application.WeatherFeedback.ChangeFeedback;

public class ChangeFeedbackCommandValidator : AbstractValidator<ChangeFeedbackCommand>
{
    public ChangeFeedbackCommandValidator()
    {
        RuleFor(sfr => sfr.Id)
            .NotEmpty();
        RuleFor(sfr => sfr.Feedback)
            .SetValidator(new FeedbackTextValidator());
        RuleFor(sfr => sfr.Rating)
            .SetValidator(new FeedbackRatingValidator());
        RuleFor(sfr => sfr.UserId)
            .NotEmpty()
                .WithMessage("UserId cannot be empty.");
    }
}
