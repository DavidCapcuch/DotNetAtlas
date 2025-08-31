using FluentValidation;

namespace DotNetAtlas.Application.Feedback.ChangeFeedback;

public class ChangeFeedbackCommandValidator : AbstractValidator<ChangeFeedbackCommand>
{
    public ChangeFeedbackCommandValidator()
    {
        RuleFor(sfr => sfr.Id)
            .NotEmpty();
        RuleFor(sfr => sfr.Feedback)
            .NotEmpty()
            .WithMessage("Feedback cannot be empty.")
            .MaximumLength(500)
            .WithMessage("Feedback cannot exceed 500 characters.");
        RuleFor(sfr => sfr.Rating)
            .InclusiveBetween((byte) 1, (byte) 5)
            .WithMessage("Rating must be between 1 and 5.");
        RuleFor(sfr => sfr.UserId)
            .NotEmpty()
            .WithMessage("UserId cannot be empty.");
    }
}
