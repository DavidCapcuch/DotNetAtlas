using System.ComponentModel.DataAnnotations;

namespace Notifications.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for Notifications readiness-probe timeouts. Mirrors the
/// 7 BC reference (Basket / Catalog / Inventory / Invoicing / Ordering / Payments).
/// </summary>
public sealed class HealthChecksOptions
{
    public const string Section = "HealthChecks";

    [Range(typeof(TimeSpan), "00:00:01", "00:00:02")]
    public required TimeSpan DbTimeout { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "00:00:04")]
    public required TimeSpan KafkaTimeout { get; set; }
}
