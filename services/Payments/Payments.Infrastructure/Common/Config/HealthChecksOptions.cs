using System.ComponentModel.DataAnnotations;

namespace Payments.Infrastructure.Common.Config;

/// <summary>
/// Configuration options for Payments readiness-probe timeouts. Mirrors the Basket / Catalog
/// precedent (<c>services/Basket/Basket.Infrastructure/Common/Config/HealthChecksOptions.cs</c>):
/// <c>AddDbContextCheck</c> does not expose a direct timeout parameter, so no DB timeout is
/// carried here. Operators who need a DB-level readiness timeout switch to <c>AddNpgSql</c>
/// or wire <c>CommandTimeout</c> into <c>EfCoreOptions</c>.
/// </summary>
public sealed class HealthChecksOptions
{
    public const string Section = "HealthChecks";

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public required TimeSpan KafkaTimeout { get; set; }
}
