using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for Hangfire health checks.
/// </summary>
public sealed class HangfireHealthCheckOptions
{
    public const string Section = $"{HealthChecksOptions.Section}:Hangfire";

    /// <summary>
    /// Timeout for Hangfire health checks.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan Timeout { get; set; }

    /// <summary>
    /// Maximum failed jobs for degraded health check.
    /// </summary>
    [Required]
    [Range(0, 100)]
    public required int DegradedMaximumJobsFailed { get; set; }

    /// <summary>
    /// Maximum failed jobs for unhealthy health check.
    /// </summary>
    [Required]
    [Range(0, 100)]
    public required int UnhealthyMaximumJobsFailed { get; set; }

    /// <summary>
    /// Minimum available servers for unhealthy health check.
    /// </summary>
    [Required]
    [Range(0, 10)]
    public required int MinimumAvailableServers { get; set; }
}
