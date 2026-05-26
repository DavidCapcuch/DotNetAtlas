using System.ComponentModel.DataAnnotations;

namespace Invoicing.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for Invoicing readiness-probe timeouts. Mirrors the Basket / Catalog
/// precedent (<c>services/Basket/Basket.Infrastructure/Common/Config/HealthChecksOptions.cs</c>).
/// The <c>AddDbContextCheck</c> EF Core extension does not expose a direct timeout parameter,
/// so <see cref="DatabaseTimeout"/> is enforced via a per-probe cancellation token in
/// <c>HealthChecksDependencyInjection</c>.
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
}
