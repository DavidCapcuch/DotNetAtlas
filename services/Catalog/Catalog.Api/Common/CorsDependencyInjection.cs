using Catalog.Api.Common.Config;

namespace Catalog.Api.Common;

internal static class CorsDependencyInjection
{
    public static IServiceCollection AddCatalogCors(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<CatalogCorsOptions>()
            .BindConfiguration(CatalogCorsOptions.Section)
            .ValidateDataAnnotations();

        var corsOptions =
            configuration.GetRequiredSection(CatalogCorsOptions.Section).Get<CatalogCorsOptions>()!;

        // Fail fast — ASP.NET throws on first preflight when wildcard origin is mixed with
        // credentials. Surfacing it at startup beats a runtime CORS rejection in browsers.
        if (corsOptions.AllowedOrigins.Contains("*") && corsOptions.AllowCredentials)
        {
            throw new InvalidOperationException(
                $"CORS configuration error in section '{CatalogCorsOptions.Section}': " +
                "AllowedOrigins=\"*\" cannot be combined with AllowCredentials=true.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy(CatalogCorsOptions.DefaultCorsPolicyName, policy =>
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
}
