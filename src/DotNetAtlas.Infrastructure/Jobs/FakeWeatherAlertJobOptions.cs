using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Jobs;

public sealed class FakeWeatherAlertJobOptions
{
    public const string Section = "Jobs:FakeWeatherAlert";

    [Required(AllowEmptyStrings = false)]
    public string Cron { get; set; } = "*/10 * * * * *"; // default every 10 seconds
}
