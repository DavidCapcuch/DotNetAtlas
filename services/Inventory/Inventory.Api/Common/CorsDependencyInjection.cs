using Inventory.Api.Common.Config;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Common;

internal static class CorsDependencyInjection
{
    public static IServiceCollection AddInventoryCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Invariants (wildcard/localhost + credentials) are enforced at startup by
        // InventoryCorsOptionsValidator via ValidateOnStart — see InventoryCorsOptions.cs.
        services.AddOptionsWithValidateOnStart<InventoryCorsOptions>()
            .BindConfiguration(InventoryCorsOptions.Section)
            .ValidateDataAnnotations();
        services.AddSingleton<IValidateOptions<InventoryCorsOptions>, InventoryCorsOptionsValidator>();

        var corsOptions =
            configuration.GetRequiredSection(InventoryCorsOptions.Section).Get<InventoryCorsOptions>()!;

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
