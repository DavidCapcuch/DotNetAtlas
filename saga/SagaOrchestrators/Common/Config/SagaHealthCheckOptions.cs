using System.ComponentModel.DataAnnotations;

namespace SagaOrchestrators.Common.Config;

/// <summary>
/// Configuration options for saga health checks.
/// </summary>
public sealed class SagaHealthCheckOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string Section = "SagaHealthCheck";

    /// <summary>
    /// Threshold in minutes for detecting stuck sagas.
    /// Sagas not updated within this period in non-final states are considered stuck.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int StuckSagaThresholdMinutes { get; set; }

    /// <summary>
    /// Number of stuck sagas that triggers degraded health status.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int MaxStuckSagasBeforeDegraded { get; set; }

    /// <summary>
    /// Number of stuck sagas that triggers unhealthy status.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int MaxStuckSagasBeforeUnhealthy { get; set; }
}
