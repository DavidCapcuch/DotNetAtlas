using System.ComponentModel.DataAnnotations;

namespace Catalog.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for Catalog readiness-probe timeouts. Mirrors the platform
/// reference at
/// <c>platform/Platform.OutboxRelay.WorkerService/Common/Config/HealthChecksOptions.cs</c>.
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

    /// <summary>
    /// HTTP timeout for the Schema-Registry <c>AddUrlGroup</c> probe (Wave-1 closeout I1 /
    /// #210). Default-cap the per-probe time so a slow SR cannot stall <c>/api/readiness</c>
    /// past the other probes' combined budget.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan SchemaRegistryTimeout { get; set; }
}
