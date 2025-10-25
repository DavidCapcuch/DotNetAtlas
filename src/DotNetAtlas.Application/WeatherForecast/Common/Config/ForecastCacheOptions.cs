using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Application.WeatherForecast.Common.Config;

public sealed class ForecastCacheOptions
{
    public const string Section = "Weather:ForecastCache";

    [Range(0, 7 * 24 * 60)]
    public int DurationMinutes { get; set; } = 720;

    public bool EnableFailSafe { get; set; } = true;

    [Range(0, 7 * 24 * 60)]
    public int FailSafeMaxDurationMinutes { get; set; } = 360;

    [Range(0, 60)]
    public int FailSafeThrottleSeconds { get; set; } = 10;

    [Range(0, 60_000)]
    public int FactorySoftTimeoutMs { get; set; } = 100;

    [Range(0, 10 * 30_000)]
    public int FactoryHardTimeoutMs { get; set; } = 2000;

    [Range(0.0, 1.0)]
    public float EagerRefreshThreshold { get; set; } = 0.9f;
}
