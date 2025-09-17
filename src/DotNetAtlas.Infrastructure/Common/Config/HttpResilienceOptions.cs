using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Common.Config;

public sealed class HttpResilienceOptions
{
    public const string Section = "HttpResilience";

    [Range(1, 600)]
    public int TotalRequestTimeoutSeconds { get; set; }

    [Range(1, 300)]
    public int AttemptTimeoutSeconds { get; set; }

    [Range(0, 10)]
    public int RetryMaxAttempts { get; set; }

    [Range(1, 3600)]
    public int CircuitBreakerSamplingSeconds { get; set; }

    [Range(1, int.MaxValue)]
    public int CircuitBreakerMinimumThroughput { get; set; }

    [Range(0.0, 1.0)]
    public double CircuitBreakerFailureRatio { get; set; }

    [Range(1, 3600)]
    public int CircuitBreakerBreakSeconds { get; set; }
}
