using System.ComponentModel.DataAnnotations;

namespace Payments.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for Payments readiness-probe timeouts. Mirrors the Basket / Catalog
/// precedent (<c>services/Basket/Basket.Infrastructure/Common/Config/HealthChecksOptions.cs</c>).
/// </summary>
public sealed class HealthChecksOptions
{
    public const string Section = "HealthChecks";

    [Range(typeof(TimeSpan), "00:00:01", "00:00:02")]
    public required TimeSpan DbTimeout { get; set; }

    [Range(typeof(TimeSpan), "00:00:01", "00:00:04")]
    public required TimeSpan KafkaTimeout { get; set; }
}
