using Platform.SharedKernel.Errors;

namespace Weather.Domain.Alerts.Errors;

public static class WeatherAlertErrors
{
    public static ValidationError AlertMessageRequired()
        => new ValidationError(
            propertyName: "AlertMessage",
            errorMessage: "Alert message cannot be null or empty.",
            errorCode: "Alert.MessageRequired");

    public static ValidationError AlertMessageTooLong(int maxLength)
        => new ValidationError(
            propertyName: "AlertMessage",
            errorMessage: $"Alert message cannot exceed {maxLength} characters.",
            errorCode: "Alert.MessageTooLong");
}
