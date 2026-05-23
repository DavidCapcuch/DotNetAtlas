using System.ComponentModel.DataAnnotations;

namespace Basket.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for Basket readiness-probe timeouts. Mirrors the platform
/// reference at
/// <c>platform/Platform.OutboxRelay.WorkerService/Common/Config/HealthChecksOptions.cs</c>
/// and the Catalog M10 precedent. <see cref="DatabaseTimeout"/> is Basket-specific —
/// applied to <c>AddDbContextCheck</c> via a per-probe cancellation token (the EF Core
/// extension does not expose a direct timeout parameter, per Wave-1 closeout #218).
/// </summary>
public sealed class HealthChecksOptions
{
    public const string Section = "HealthChecks";

    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan SelfTimeout { get; set; }

    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan DatabaseTimeout { get; set; }

    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan KafkaTimeout { get; set; }

    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan RedisTimeout { get; set; }
}
