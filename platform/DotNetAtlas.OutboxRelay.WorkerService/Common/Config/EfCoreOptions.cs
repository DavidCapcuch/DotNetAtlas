using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.OutboxRelay.WorkerService.Common.Config;

public sealed class EfCoreOptions
{
    public const string Section = "EfCore";

    [Required]
    [Range(1, 10)]
    public required int DbContextPoolSize { get; set; }

    [Required]
    [Range(0, 10)]
    public required int RetryMaxCount { get; set; }

    [Required]
    [Range(1, 180)]
    public required int RetryMaxDelaySeconds { get; set; }

    [Required]
    public required bool EnableDetailedErrors { get; set; }
}
