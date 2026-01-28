using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Entity Framework Core configuration options.
/// </summary>
public sealed class EfCoreOptions
{
    public const string Section = "EfCore";

    [Required]
    [Range(0, 10)]
    public required int RetryMaxCount { get; set; }

    [Required]
    [Range(1, 180)]
    public required int RetryMaxDelaySeconds { get; set; }

    [Required]
    public required bool EnableDetailedErrors { get; set; }
}
