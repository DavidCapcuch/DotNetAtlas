namespace DotNetAtlas.Api.Common.Config;

public class CorsPolicyOptions
{
    public const string DefaultCorsPolicyName = "DefaultCorsPolicy";
    public const string Section = "Cors";

    public required string[] AllowedOrigins { get; set; }
    public required string[] AllowedMethods { get; set; }
    public required string[] AllowedHeaders { get; set; }
    public required string[] ExposedHeaders { get; set; }

    public bool AllowCredentials { get; set; }
}
