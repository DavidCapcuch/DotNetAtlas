using DotNetAtlas.Domain.Feedback.ValueObjects;
using DotNetAtlas.SharedKernel.Errors;

namespace DotNetAtlas.Domain.Feedback.Errors;

public static class FeedbackErrors
{
    public static ValidationError FeedbackRequired()
        => new ValidationError(
            propertyName: nameof(Feedback.FeedbackText),
            errorMessage: "Feedback text cannot be null or empty.",
            errorCode: "Feedback.TextRequired");

    public static ValidationError FeedbackTooLong(int maxLength)
        => new ValidationError(
            propertyName: nameof(Feedback.FeedbackText),
            errorMessage: $"Feedback text cannot exceed {maxLength} characters.",
            errorCode: "Feedback.TextTooLong");

    public static ValidationError OutOfRange(int minInclusive, int maxInclusive)
        => new ValidationError(
            propertyName: nameof(FeedbackRating.Value),
            errorMessage: $"Rating must be between {minInclusive} and {maxInclusive}.",
            errorCode: "Feedback.RatingOutOfRange");

    public static NotFoundError NotFound(Guid id)
        => new NotFoundError(nameof(Feedback), id, "WeatherFeedback.NotFound");

    public static ForbiddenError Forbidden(Guid id)
        => new ForbiddenError(nameof(Feedback), id, "WeatherFeedback.Forbidden");

    public static ConflictError Conflict(Guid id)
        => new ConflictError(
            nameof(Feedback),
            $"User already created feedback with id {id}",
            "Feedback.Conflict");
}
