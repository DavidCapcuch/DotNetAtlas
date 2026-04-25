using System.ComponentModel.DataAnnotations;

namespace Basket.Infrastructure.Common.Config;

/// <summary>
/// EF Core runtime knobs bound from the <c>EfCore</c> configuration section.
/// Applies only to the Basket service's SQL side-car (<c>BasketDbContext</c>)
/// which carries outbox + inbox tables exclusively per ADR-0003.
/// </summary>
public sealed class EfCoreOptions
{
    public const string Section = "EfCore";

    [Required]
    public required bool UseQuerySplitting { get; set; }

    [Required]
    [Range(0, 10)]
    public required int RetryMaxCount { get; set; }

    [Required]
    [Range(1, 180)]
    public required int RetryMaxDelaySeconds { get; set; }

    [Required]
    public required bool EnableDetailedErrors { get; set; }
}
