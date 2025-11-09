using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.OutboxRelay.WorkerService.Observability.Metrics;

/// <summary>
/// Configuration options for outbox metrics monitoring.
/// </summary>
public sealed class OutboxMetricsCollectorOptions
{
    public const string Section = "OutboxMetricsCollector";

    /// <summary>
    /// Interval in seconds at which to update outbox size metrics.
    /// </summary>
    [Required]
    [Range(1, 300, ErrorMessage = "Metrics reporting interval must be between 1 and 300 seconds")]
    public required int ReportIntervalSeconds { get; set; }
}
