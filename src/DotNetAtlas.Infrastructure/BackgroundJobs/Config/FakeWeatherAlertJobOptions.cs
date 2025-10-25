using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.BackgroundJobs.Config;

public sealed class FakeWeatherAlertJobOptions
{
    public const string Section = "Jobs:FakeWeatherAlert";

    [Required(AllowEmptyStrings = false)]
    public required string Cron { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string Queue { get; set; }
}
