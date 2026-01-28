using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Entity Framework Core configuration options.
/// </summary>
public sealed class EfCoreOptions
{
    public const string Section = "EfCore";

    /// <summary>
    /// Size of the DbContext pool.
    /// </summary>
    [Required]
    [Range(1, 1024)]
    public required int DbContextPoolSize { get; set; }

    /// <summary>
    /// Maximum retry count for transient database failures.
    /// </summary>
    [Required]
    [Range(0, 10)]
    public required int RetryMaxCount { get; set; }

    /// <summary>
    /// Maximum delay in seconds between retry attempts.
    /// </summary>
    [Required]
    [Range(1, 180)]
    public required int RetryMaxDelaySeconds { get; set; }

    /// <summary>
    /// Enable detailed EF Core errors (recommended for development only).
    /// </summary>
    [Required]
    public required bool EnableDetailedErrors { get; set; }
}
