using DotNetAtlas.Domain.Alerts.Errors;
using DotNetAtlas.SharedKernel.Base;
using FluentResults;

namespace DotNetAtlas.Domain.Alerts.ValueObjects;

/// <summary>
/// Value object representing an alert condition that was triggered.
/// Contains the type, severity, and a human-readable message.
/// This is a pure data holder - severity determination logic belongs in the aggregate.
/// </summary>
public sealed record WeatherAlert : ValueObject
{
    public const int MaxMessageLength = 500;

    public AlertType Type { get; private init; }
    public AlertSeverity Severity { get; private init; }
    public string Message { get; private init; } = null!;

    private WeatherAlert()
    {
    }

    /// <summary>
    /// Creates a new alert condition with the specified values.
    /// </summary>
    /// <param name="type">The type of alert.</param>
    /// <param name="severity">The severity level of the alert.</param>
    /// <param name="message">A human-readable description of the alert.</param>
    /// <returns>A new AlertCondition instance.</returns>
    public static Result<WeatherAlert> Create(AlertType type, AlertSeverity severity, string? message)
    {
        message = message?.Trim();

        var mergedResults = Result.Merge(
            Result.FailIf(string.IsNullOrWhiteSpace(message), WeatherAlertErrors.AlertMessageRequired()),
            Result.FailIf(message?.Length > MaxMessageLength,
                WeatherAlertErrors.AlertMessageTooLong(MaxMessageLength)));

        if (mergedResults.IsFailed)
        {
            return Result.Fail<WeatherAlert>(mergedResults.Errors);
        }

        return new WeatherAlert
        {
            Type = type,
            Severity = severity,
            Message = message!
        };
    }
}
