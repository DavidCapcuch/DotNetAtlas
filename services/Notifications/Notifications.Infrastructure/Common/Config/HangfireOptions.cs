using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Common.Config;

/// <summary>
/// Hangfire storage + server tuning for the per-channel dispatch jobs (ADR-0032). Mirrors the
/// <c>src/Weather</c> Hangfire template; Hangfire stores its tables in the <c>notifications</c> DB.
/// </summary>
public sealed class HangfireOptions
{
    public const string Section = "Hangfire";

    [Required]
    [Range(1, int.MaxValue)]
    public required int JobExpirationCheckIntervalMs { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public required int QueuePollIntervalMs { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public required int SchedulePollingIntervalMs { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public required int CancellationCheckIntervalMs { get; set; }

    [Required]
    [MinLength(1)]
    public required string[] Queues { get; set; }
}
