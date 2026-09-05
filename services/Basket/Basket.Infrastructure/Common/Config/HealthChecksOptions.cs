using System.ComponentModel.DataAnnotations;

namespace Basket.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for Basket readiness-probe timeouts. Mirrors the platform
/// reference at
/// <c>platform/Platform.OutboxRelay.WorkerService/Common/Config/HealthChecksOptions.cs</c>
/// and the Catalog precedent.
/// <see cref="RedisTimeout"/> is shared by the redis-basket and
/// redis-cache probes.
/// </summary>
public sealed class HealthChecksOptions
{
    public const string Section = "HealthChecks";

    [Range(typeof(TimeSpan), "00:00:01", "00:00:02")]
    public required TimeSpan DbTimeout { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "00:00:01")]
    public required TimeSpan RedisTimeout { get; set; }
}
