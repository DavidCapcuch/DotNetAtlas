using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Common.Config;

public sealed class EfCoreOptions
{
    public const string Section = "EfCore";

    [Required]
    [Range(1, 4096)]
    public required int DbContextPoolSize { get; set; }

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
