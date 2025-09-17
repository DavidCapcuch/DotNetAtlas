using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Api.Common.Config;

public class CorsPolicyOptions
{
    public const string DefaultCorsPolicyName = "DefaultCorsPolicy";
    public const string Section = "Cors";

    [Required]
    [MinLength(1)]
    public required string[] AllowedOrigins { get; set; }

    [Required]
    public required bool AllowWildcardSubdomains { get; set; }

    [Required]
    [MinLength(1)]
    public required string[] AllowedMethods { get; set; }

    [Required]
    [MinLength(1)]
    public required string[] AllowedHeaders { get; set; }

    public required string[] ExposedHeaders { get; set; }

    public bool AllowCredentials { get; set; }
}
