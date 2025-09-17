using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Application.Common.Config;

public sealed class WeatherHedgingOptions
{
    public const string Section = "Weather:Hedging";

    [Range(1, 1_000)]
    public int PrimaryMaxDurationMs { get; set; }
}
