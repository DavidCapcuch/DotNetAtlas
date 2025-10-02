using DotNetAtlas.Application.Feedback.Common.Validation;
using FluentValidation;

namespace DotNetAtlas.Application.Feedback.ChangeFeedback;

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
