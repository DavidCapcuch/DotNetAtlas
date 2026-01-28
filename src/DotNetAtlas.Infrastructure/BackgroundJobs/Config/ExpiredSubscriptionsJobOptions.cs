using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.BackgroundJobs.Config;

public sealed class ExpiredSubscriptionsJobOptions
{
    public const string Section = "Jobs:ExpiredSubscriptions";

    [Required(AllowEmptyStrings = false)]
    public required string Cron { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string Queue { get; set; }

    /// <summary>
    /// Maximum number of expired subscriptions to process per job execution.
    /// </summary>
    [Required]
    [Range(1, 10_000)]
    public required int BatchSize { get; set; }
}
