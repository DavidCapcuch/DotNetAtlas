using Catalog.Api.Common.Config;
using Microsoft.Extensions.Options;

namespace Catalog.Api.Common;

internal static class CorsDependencyInjection
{
    public static IServiceCollection AddCatalogCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Invariants (wildcard/localhost + credentials) are enforced at startup by
        // CatalogCorsOptionsValidator via ValidateOnStart — see CatalogCorsOptions.cs.
        services.AddOptionsWithValidateOnStart<CatalogCorsOptions>()
            .BindConfiguration(CatalogCorsOptions.Section)
            .ValidateDataAnnotations();
        services.AddSingleton<IValidateOptions<CatalogCorsOptions>, CatalogCorsOptionsValidator>();

        var corsOptions =
            configuration.GetRequiredSection(CatalogCorsOptions.Section).Get<CatalogCorsOptions>()!;

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
