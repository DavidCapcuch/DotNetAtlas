using System.ComponentModel.DataAnnotations;

namespace Payments.Infrastructure.Common.Config;

/// <summary>
/// EF Core runtime knobs bound from <c>EfCore</c> configuration section.
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
