using DotNetAtlas.Domain.Entities.Weather;
using DotNetAtlas.Domain.Errors.Base;

namespace DotNetAtlas.Domain.Errors
{
    public static class FeedbackRatingErrors
    {
        public static ValidationError OutOfRange(int minInclusive, int maxInclusive)
            => new ValidationError(
                propertyName: nameof(FeedbackRating.Value),
                errorMessage: $"Rating must be between {minInclusive} and {maxInclusive}.",
                errorCode: "FeedbackRating.OutOfRange");
    }
}