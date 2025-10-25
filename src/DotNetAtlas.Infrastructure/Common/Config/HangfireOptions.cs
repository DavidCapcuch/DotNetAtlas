using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Common.Config;

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
