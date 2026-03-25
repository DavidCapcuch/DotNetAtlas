namespace Weather.Domain.Alerts.ValueObjects;

/// <summary>
/// Severity levels for weather alerts.
/// </summary>
public enum AlertSeverity
{
    /// <summary>
    /// Informational alert, no immediate action required.
    /// </summary>
    Info,

    /// <summary>
    /// Warning alert, conditions may become hazardous.
    /// </summary>
    Warning,

    /// <summary>
    /// Critical alert, immediate attention recommended.
    /// </summary>
    Critical
}
