using System.ComponentModel.DataAnnotations;

namespace Basket.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for Basket readiness-probe timeouts. Mirrors the platform
/// reference at
/// <c>platform/Platform.OutboxRelay.WorkerService/Common/Config/HealthChecksOptions.cs</c>
/// and the Catalog precedent: <c>AddDbContextCheck</c> does not expose a direct
/// timeout parameter, so no DB timeout is carried here. Operators who need a DB-level
/// readiness timeout switch to <c>AddNpgSql</c> or wire <c>CommandTimeout</c> into
/// <c>EfCoreOptions</c>. No <c>KafkaTimeout</c> — Basket has no in-process Kafka client and
/// deliberately does not probe the broker (publish is 100% outbox + <c>outbox-relay-basket</c>,
/// which owns broker readiness). <see cref="RedisTimeout"/> is shared by the redis-basket and
/// redis-cache probes.
/// </summary>
public sealed class HealthChecksOptions
{
    public const string Section = "HealthChecks";

    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan SelfTimeout { get; set; }

    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan RedisTimeout { get; set; }
}
