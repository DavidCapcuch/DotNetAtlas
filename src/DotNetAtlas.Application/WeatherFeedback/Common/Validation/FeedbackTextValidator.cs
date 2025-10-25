using FluentValidation;

namespace DotNetAtlas.Application.WeatherFeedback.Common.Validation;

public sealed class FeedbackTextValidator : AbstractValidator<string>
{
    public FeedbackTextValidator()
    {
        RuleFor(text => text)
            .NotEmpty().WithMessage("Feedback cannot be empty.")
            .MaximumLength(500).WithMessage("Feedback cannot exceed 500 characters.");
    }
}
