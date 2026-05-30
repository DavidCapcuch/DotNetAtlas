using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults;

namespace Inventory.Api.Common.Config;

internal sealed class InventoryCorsOptions
{
    public const string DefaultCorsPolicyName = "InventoryCorsPolicy";
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

/// <summary>
/// Startup validation for <see cref="InventoryCorsOptions"/> (run via
/// <c>AddOptionsWithValidateOnStart</c>). Rejects wildcard-origin + credentials in every
/// environment (ASP.NET throws on the first preflight otherwise), and localhost-origin +
/// credentials once deployed — a session-leak vector that must not survive a stale dev
/// config shipping to staging/prod.
/// </summary>
internal sealed class InventoryCorsOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<InventoryCorsOptions>
{
    public ValidateOptionsResult Validate(string? name, InventoryCorsOptions options)
    {
        var origins = options.AllowedOrigins;
        if (origins is null || origins.Length == 0)
        {
            // Shape errors (required / min-length) are surfaced by ValidateDataAnnotations.
            return ValidateOptionsResult.Success;
        }

        if (origins.Contains("*") && options.AllowCredentials)
        {
            return ValidateOptionsResult.Fail(
                $"CORS configuration error in section '{InventoryCorsOptions.Section}': " +
                "AllowedOrigins=\"*\" cannot be combined with AllowCredentials=true.");
        }

        if (environment.IsDeployedEnvironment() && options.AllowCredentials)
        {
            foreach (var origin in origins)
            {
                if (IsLocalhostOrigin(origin))
                {
                    return ValidateOptionsResult.Fail(
                        $"CORS configuration error in section '{InventoryCorsOptions.Section}': " +
                        $"localhost origin '{origin}' cannot be combined with " +
                        "AllowCredentials=true in deployed environments.");
                }
            }
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsLocalhostOrigin(string origin) =>
        !string.IsNullOrEmpty(origin)
        && (origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase));
}
