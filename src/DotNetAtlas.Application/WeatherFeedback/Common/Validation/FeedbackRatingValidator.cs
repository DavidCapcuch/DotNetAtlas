using DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;
using FluentValidation;

namespace DotNetAtlas.Application.WeatherFeedback.Common.Validation;

public sealed class FeedbackRatingValidator : AbstractValidator<byte>
{
    public FeedbackRatingValidator()
    {
        RuleFor(r => r)
            .InclusiveBetween((byte)FeedbackRating.MinimumRating, (byte)FeedbackRating.MaximumRating)
            .WithMessage($"Rating must be between {FeedbackRating.MinimumRating} and {FeedbackRating.MaximumRating}.");
    }
}
