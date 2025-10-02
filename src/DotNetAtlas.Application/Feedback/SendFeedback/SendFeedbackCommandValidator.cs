using DotNetAtlas.Application.Feedback.Common.Validation;
using FluentValidation;

namespace DotNetAtlas.Application.Feedback.SendFeedback;

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
