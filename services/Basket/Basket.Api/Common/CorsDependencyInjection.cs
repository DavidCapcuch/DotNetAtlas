using Basket.Api.Common.Config;
using Microsoft.Extensions.Options;

namespace Basket.Api.Common;

internal static class CorsDependencyInjection
{
    public static IServiceCollection AddBasketCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Invariants (wildcard/localhost + credentials) are enforced at startup by
        // BasketCorsOptionsValidator via ValidateOnStart — see BasketCorsOptions.cs.
        services.AddOptionsWithValidateOnStart<BasketCorsOptions>()
            .BindConfiguration(BasketCorsOptions.Section)
            .ValidateDataAnnotations();
        services.AddSingleton<IValidateOptions<BasketCorsOptions>, BasketCorsOptionsValidator>();

        var corsOptions =
            configuration.GetRequiredSection(BasketCorsOptions.Section).Get<BasketCorsOptions>()!;

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
}
