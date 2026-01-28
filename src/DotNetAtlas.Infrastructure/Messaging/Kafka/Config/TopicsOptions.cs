using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Kafka topic names.
/// </summary>
public sealed class TopicsOptions
{
    public const string Section = "Topics";

    /// <summary>
    /// Topic for forecast requested events.
    /// </summary>
    [Required]
    [Length(1, 249)]
    public required string ForecastRequested { get; set; }

    /// <summary>
    /// Topic for Weather Alerts commands.
    /// Consumed by WeatherAlerts service for subscription management:
    /// - ActivateSubscriptionCommand (from Purchase Saga)
    /// - ExtendSubscriptionCommand (from Extension Saga).
    /// </summary>
    [Required]
    [Length(1, 249)]
    public required string WeatherAlertsCommands { get; set; }

    /// <summary>
    /// Suffix appended to topic names to create Dead Letter Topics (e.g., ".DLT").
    /// </summary>
    [Required]
    [Length(1, 64)]
    public required string DltTopicSuffix { get; set; }
}
