using FluentValidation;

namespace DotNetAtlas.Application.Feedback.Common.Validation;

public sealed class FeedbackRatingValidator : AbstractValidator<byte>
{
    public FeedbackRatingValidator()
    {
        RuleFor(r => r)
            .InclusiveBetween((byte)1, (byte)5)
            .WithMessage("Rating must be between 1 and 5.");
    }
}
