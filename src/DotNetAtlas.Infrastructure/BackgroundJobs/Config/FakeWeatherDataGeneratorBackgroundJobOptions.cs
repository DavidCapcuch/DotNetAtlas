using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.BackgroundJobs.Config;

public sealed class FakeWeatherDataGeneratorBackgroundJobOptions
{
    public const string Section = "Jobs:FakeWeatherDataGeneratorBackgroundJob";

    [Required(AllowEmptyStrings = false)]
    public required string Cron { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string Queue { get; set; }

    /// <summary>
    /// Number of weather readings to generate in each batch.
    /// </summary>
    [Required]
    [Range(1, 10_000)]
    public required int BatchSize { get; set; }
}
