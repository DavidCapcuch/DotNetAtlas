using System.ComponentModel.DataAnnotations;

namespace SagaOrchestrators.Common.Config;

/// <summary>
/// Configuration for stuck-saga detection: what counts as stuck, how often the sweep looks, and
/// how many stuck sagas make readiness report degraded.
/// </summary>
public sealed class StuckSagaOptions
{
    public const string Section = "StuckSaga";

    /// <summary>
    /// A saga in a non-terminal state and untouched for this long is counted as stuck.
    /// </summary>
    [Range(1, int.MaxValue)]
    public required int StuckSagaThresholdMinutes { get; set; }

    /// <summary>
    /// Interval between sweeps. The published count is stale by up to this long, which is why
    /// nothing that gates traffic may depend on it.
    /// </summary>
    [Range(1, 300)]
    public required int SweepIntervalSeconds { get; set; }

    /// <summary>
    /// Stuck-saga count at which readiness reports degraded. There is deliberately no unhealthy
    /// band above it: every replica counts the same rows, so an unhealthy verdict would drop them
    /// all from rotation at once while none of them is broken.
    /// </summary>
    [Range(1, int.MaxValue)]
    public required int MaxStuckSagasBeforeDegraded { get; set; }
}
