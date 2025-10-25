using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Common.Config;

public sealed class DefaultCacheOptions
{
    public const string Section = "DefaultCache";

    [Range(0, 3600)]
    public int DistributedCacheCircuitBreakerSeconds { get; set; }

    public bool IncludeTagsInLogs { get; set; }
    public bool IncludeTagsInTraces { get; set; }
    public bool IncludeTagsInMetrics { get; set; }

    [Range(0, 7 * 24 * 60)]
    public int DefaultDurationMinutes { get; set; }

    [Range(0, 30_000)]
    public int FactorySoftTimeoutMs { get; set; }

    [Range(0, 60_000)]
    public int FactoryHardTimeoutMs { get; set; }

    [Range(0, 10)]
    public int DistributedCacheSoftTimeoutSeconds { get; set; }

    [Range(0, 20)]
    public int DistributedCacheHardTimeoutSeconds { get; set; }

    public bool AllowBackgroundDistributedCacheOperations { get; set; }
    public bool AllowBackgroundBackplaneOperations { get; set; }

    [Range(0, 20)]
    public int JitterMaxDurationSeconds { get; set; }
}
