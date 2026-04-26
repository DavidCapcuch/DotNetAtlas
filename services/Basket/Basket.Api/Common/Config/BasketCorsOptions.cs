using System.ComponentModel.DataAnnotations;

namespace Basket.Api.Common.Config;

internal sealed class BasketCorsOptions
{
    public const string DefaultCorsPolicyName = "BasketCorsPolicy";
    public const string Section = "Cors";

    [Required]
    [MinLength(1)]
    public required string[] AllowedOrigins { get; set; }

    [Required]
    [MinLength(1)]
    public required string[] AllowedMethods { get; set; }

    [Required]
    [MinLength(1)]
    public required string[] AllowedHeaders { get; set; }

    public string[] ExposedHeaders { get; set; } = [];

    public bool AllowCredentials { get; set; }
}
