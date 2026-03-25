using FluentValidation;
using Weather.Application.WeatherFeedback.Common.Validation;

namespace Weather.Application.WeatherFeedback.SendFeedback;

public class SendFeedbackCommandValidator : AbstractValidator<SendFeedbackCommand>
{
    public SendFeedbackCommandValidator()
    {
        RuleFor(sfr => sfr.Feedback)
            .SetValidator(new FeedbackTextValidator());
        RuleFor(sfr => sfr.Rating)
            .SetValidator(new FeedbackRatingValidator());
        RuleFor(sfr => sfr.UserId)
            .NotEmpty()
                .WithMessage("UserId cannot be empty.");
    }
}
