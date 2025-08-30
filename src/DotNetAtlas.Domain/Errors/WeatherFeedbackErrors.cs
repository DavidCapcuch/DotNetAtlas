using DotNetAtlas.Domain.Entities.Weather;
using DotNetAtlas.Domain.Errors.Base;

namespace DotNetAtlas.Domain.Errors
{
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
    }
}