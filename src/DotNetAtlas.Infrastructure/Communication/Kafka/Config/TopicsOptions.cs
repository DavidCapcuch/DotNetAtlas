using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Communication.Kafka.Config;

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
}
