using FluentValidation;
using Weather.Domain.Feedback.ValueObjects;

namespace Weather.Application.WeatherFeedback.Common.Validation;

public sealed class FeedbackTextValidator : AbstractValidator<string>
{
    public FeedbackTextValidator()
    {
        RuleFor(text => text)
            .NotEmpty()
                .WithMessage("Feedback cannot be empty.")
            .MaximumLength(FeedbackText.TextMaxLength)
                .WithMessage($"Feedback cannot exceed {FeedbackText.TextMaxLength} characters.");
    }
}
