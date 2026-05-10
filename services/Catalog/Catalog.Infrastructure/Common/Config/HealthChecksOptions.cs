using System.ComponentModel.DataAnnotations;

namespace Catalog.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for Catalog readiness-probe timeouts. Mirrors the platform
/// reference at
/// <c>platform/Platform.OutboxRelay.WorkerService/Common/Config/HealthChecksOptions.cs</c>.
/// Only the timeouts whose underlying <c>.AddXxx</c> registrations accept a
/// <c>timeout</c> parameter are exposed: <see cref="SelfTimeout"/> for
/// <c>AddApplicationStatus</c>, <see cref="KafkaTimeout"/> for <c>AddKafka</c>, and
/// <see cref="RedisTimeout"/> for <c>AddRedis</c>. <c>AddDbContextCheck</c> does not
/// take a timeout (the EF command timeout governs it instead), and <c>AddUrlGroup</c>
/// for the Schema Registry uses its own internal HTTP-client timeout — neither is bound
/// here to avoid silently-ignored config keys.
/// </summary>
public sealed class HealthChecksOptions
{
    public const string Section = "HealthChecks";

    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan SelfTimeout { get; set; }

    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan KafkaTimeout { get; set; }

    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan RedisTimeout { get; set; }
}
