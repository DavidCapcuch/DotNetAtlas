using DotNetAtlas.Domain.Entities.Weather.Feedback;
using DotNetAtlas.Domain.Errors.Base;

namespace DotNetAtlas.Domain.Errors;

public static class WeatherFeedbackErrors
{
    public static ValidationError FeedbackRequired()
        => new ValidationError(
            propertyName: nameof(WeatherFeedback.Feedback),
            errorMessage: "Feedback cannot be null or empty.",
            errorCode: "WeatherFeedback.FeedbackRequired");

    public static ValidationError FeedbackTooLong(int maxLength)
        => new ValidationError(
            propertyName: nameof(WeatherFeedback.Feedback),
            errorMessage: $"Feedback cannot exceed {maxLength} characters.",
            errorCode: "WeatherFeedback.FeedbackTooLong");

    public static NotFoundError NotFound(Guid id)
        => new NotFoundError(nameof(WeatherFeedback), id, "WeatherFeedback.NotFound");

    public static ForbiddenError Forbidden(Guid id)
        => new ForbiddenError(nameof(WeatherFeedback), id, "WeatherFeedback.Forbidden");

    public static ConflictError Conflict(Guid id)
        => new ConflictError(
            nameof(WeatherFeedback),
            $"User already created feedback with id {id}",
            "WeatherFeedback.Conflict");
}
