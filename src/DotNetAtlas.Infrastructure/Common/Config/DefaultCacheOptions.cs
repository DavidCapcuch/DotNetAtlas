using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Common.Config;

public sealed class DefaultCacheOptions
{
    public const string Section = "DefaultCache";

    [Range(0, 3600)]
    public int DistributedCacheCircuitBreakerSeconds { get; set; } = 2;

    public bool IncludeTagsInLogs { get; set; } = true;
    public bool IncludeTagsInTraces { get; set; } = true;
    public bool IncludeTagsInMetrics { get; set; } = true;

    [Range(0, 7 * 24 * 60)]
    public int DefaultDurationMinutes { get; set; } = 1;

    [Range(0, 30_000)]
    public int FactorySoftTimeoutMs { get; set; } = 300;

    [Range(0, 60_000)]
    public int FactoryHardTimeoutMs { get; set; } = 1500;

    [Range(0, 10)]
    public int DistributedCacheSoftTimeoutSeconds { get; set; } = 1;

    [Range(0, 20)]
    public int DistributedCacheHardTimeoutSeconds { get; set; } = 2;

    public bool AllowBackgroundDistributedCacheOperations { get; set; } = true;
    public bool AllowBackgroundBackplaneOperations { get; set; } = true;

    [Range(0, 20)]
    public int JitterMaxDurationSeconds { get; set; } = 2;
}
