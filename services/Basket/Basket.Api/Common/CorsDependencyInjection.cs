using Basket.Api.Common.Config;
using Microsoft.Extensions.Hosting;
using Platform.ServiceDefaults;

namespace Basket.Api.Common;

internal static class CorsDependencyInjection
{
    public static IServiceCollection AddBasketCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptionsWithValidateOnStart<BasketCorsOptions>()
            .BindConfiguration(BasketCorsOptions.Section)
            .ValidateDataAnnotations();

        var corsOptions =
            configuration.GetRequiredSection(BasketCorsOptions.Section).Get<BasketCorsOptions>()!;

        // Wildcard-with-credentials is a configuration error in every environment —
        // ASP.NET will throw "The CORS protocol does not allow specifying a wildcard
        // (any) origin and credentials at the same time" on the first preflight; we
        // surface it at startup instead.
        if (corsOptions.AllowedOrigins.Contains("*") && corsOptions.AllowCredentials)
        {
            throw new InvalidOperationException(
                $"CORS configuration error in section '{BasketCorsOptions.Section}': " +
                "AllowedOrigins=\"*\" cannot be combined with AllowCredentials=true.");
        }

        // localhost-with-credentials is fine in dev/test but a session-leak vector
        // once deployed — fail fast so a stale dev config cannot ship to staging/prod.
        if (environment.IsDeployedEnvironment())
        {
            AssertDeployedCorsOptions(corsOptions);
        }

        services.AddCors(options =>
        {
            options.AddPolicy(BasketCorsOptions.DefaultCorsPolicyName, policy =>
            {
                if (corsOptions.AllowedOrigins.Contains("*"))
                {
                    policy.AllowAnyOrigin();
                }
                else
                {
                    policy.WithOrigins(corsOptions.AllowedOrigins);
                }

                if (corsOptions.AllowCredentials)
                {
                    policy.AllowCredentials();
                }

                if (corsOptions.AllowedMethods.Contains("*"))
                {
                    policy.AllowAnyMethod();
                }
                else
                {
                    policy.WithMethods(corsOptions.AllowedMethods);
                }

                if (corsOptions.AllowedHeaders.Contains("*"))
                {
                    policy.AllowAnyHeader();
                }
                else
                {
                    policy.WithHeaders(corsOptions.AllowedHeaders);
                }

                if (corsOptions.ExposedHeaders is { Length: > 0 })
                {
                    policy.WithExposedHeaders(corsOptions.ExposedHeaders);
                }
            });
        });

        return services;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if any configured origin is
    /// a <c>localhost</c> URL while <c>AllowCredentials = true</c>. Extracted from
    /// <see cref="AddBasketCors"/> so the deployed-env invariant can be unit-tested
    /// without standing up the DI container.
    /// </summary>
    internal static void AssertDeployedCorsOptions(BasketCorsOptions corsOptions)
    {
        if (!corsOptions.AllowCredentials)
        {
            return;
        }

        foreach (var origin in corsOptions.AllowedOrigins)
        {
            if (IsLocalhostOrigin(origin))
            {
                throw new InvalidOperationException(
                    $"CORS configuration error in section '{BasketCorsOptions.Section}': " +
                    $"localhost origin '{origin}' cannot be combined with " +
                    "AllowCredentials=true in deployed environments.");
            }
        }
    }

    private static bool IsLocalhostOrigin(string origin) =>
        !string.IsNullOrEmpty(origin)
        && (origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase));
}
