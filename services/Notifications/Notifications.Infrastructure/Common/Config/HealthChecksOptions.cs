using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for Notifications readiness-probe timeouts. Mirrors the
/// 7 BC reference (Basket / Catalog / Inventory / Invoicing / Ordering / Payments).
/// <c>AddDbContextCheck</c> does not expose a direct timeout parameter, so no DB
/// timeout is carried here — the DB readiness probe runs under EF's command-timeout
/// default.
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
}
