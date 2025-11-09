using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Common.Config;

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

    /// <summary>
    /// Timeout for Redis health checks.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan RedisTimeout { get; set; }

    /// <summary>
    /// Timeout for IDM (Identity Management) API health checks.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan IdmApiTimeout { get; set; }

    /// <summary>
    /// Timeout for External Providers API health checks.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan ExternalProvidersApiTimeout { get; set; }

    /// <summary>
    /// Configuration for Hangfire health checks.
    /// </summary>
    [Required]
    public required HangfireHealthCheckOptions Hangfire { get; set; }
}
