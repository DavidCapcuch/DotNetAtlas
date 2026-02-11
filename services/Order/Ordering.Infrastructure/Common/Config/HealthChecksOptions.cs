using System.ComponentModel.DataAnnotations;

namespace Ordering.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for health check timeouts.
/// </summary>
public sealed class HealthChecksOptions
{
    public const string Section = "HealthChecks";

    /// <summary>
    /// Timeout for Self health check.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan SelfTimeout { get; set; }

    /// <summary>
    /// Timeout for SQL Server health checks.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan SqlServerTimeout { get; set; }

    /// <summary>
    /// Timeout for Kafka health checks.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan KafkaTimeout { get; set; }
}
