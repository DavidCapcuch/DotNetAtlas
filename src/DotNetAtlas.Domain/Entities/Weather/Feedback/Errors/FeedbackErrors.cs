using DotNetAtlas.Domain.Common.Errors;

namespace DotNetAtlas.Domain.Entities.Weather.Feedback.Errors;

public static class FeedbackErrors
{
    public static ValidationError FeedbackRequired()
        => new ValidationError(
            propertyName: nameof(Feedback.FeedbackText),
            errorMessage: "Feedback cannot be null or empty.",
            errorCode: "WeatherFeedback.FeedbackRequired");

    public static ValidationError FeedbackTooLong(int maxLength)
        => new ValidationError(
            propertyName: nameof(Feedback.FeedbackText),
            errorMessage: $"Feedback cannot exceed {maxLength} characters.",
            errorCode: "WeatherFeedback.FeedbackTooLong");

    public static NotFoundError NotFound(Guid id)
        => new NotFoundError(nameof(Feedback), id, "WeatherFeedback.NotFound");

    public static ForbiddenError Forbidden(Guid id)
        => new ForbiddenError(nameof(Feedback), id, "WeatherFeedback.Forbidden");

    public static ConflictError Conflict(Guid id)
        => new ConflictError(
            nameof(Feedback),
            $"User already created feedback with id {id}",
            "WeatherFeedback.Conflict");
}
