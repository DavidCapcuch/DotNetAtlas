using DotNetAtlas.Domain.Common.Errors;
using DotNetAtlas.Domain.Entities.Weather.Feedback.ValueObjects;

namespace DotNetAtlas.Domain.Entities.Weather.Feedback.Errors;

public static class FeedbackRatingErrors
{
    public static ValidationError OutOfRange(int minInclusive, int maxInclusive)
        => new ValidationError(
            propertyName: nameof(FeedbackRating.Value),
            errorMessage: $"Rating must be between {minInclusive} and {maxInclusive}.",
            errorCode: "FeedbackRating.OutOfRange");
}
