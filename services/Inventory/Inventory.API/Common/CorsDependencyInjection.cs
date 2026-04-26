using Inventory.API.Common.Config;

namespace Inventory.API.Common;

internal static class CorsDependencyInjection
{
    public static IServiceCollection AddInventoryCors(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<InventoryCorsOptions>()
            .BindConfiguration(InventoryCorsOptions.Section)
            .ValidateDataAnnotations();

        var corsOptions =
            configuration.GetRequiredSection(InventoryCorsOptions.Section).Get<InventoryCorsOptions>()!;

        // Fail fast if a future ops change crosses the wildcard-with-credentials wire.
        // ASP.NET will throw "The CORS protocol does not allow specifying a wildcard
        // (any) origin and credentials at the same time" on the first preflight; we
        // surface it at startup instead.
        if (corsOptions.AllowedOrigins.Contains("*") && corsOptions.AllowCredentials)
        {
            throw new InvalidOperationException(
                $"CORS configuration error in section '{InventoryCorsOptions.Section}': " +
                "AllowedOrigins=\"*\" cannot be combined with AllowCredentials=true.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy(InventoryCorsOptions.DefaultCorsPolicyName, policy =>
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
