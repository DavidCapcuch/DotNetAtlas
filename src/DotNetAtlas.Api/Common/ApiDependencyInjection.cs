using DotNetAtlas.Api.Common.Config;
using DotNetAtlas.Api.Common.Exceptions;
using DotNetAtlas.Api.Common.Swagger;
using FastEndpoints;
using FastEndpoints.ClientGen.Kiota;
using Kiota.Builder;

namespace DotNetAtlas.Api.Common;

public static class ApiDependencyInjection
{
    /// <summary>
    /// Maps client generation APIs for each supported <see cref="GenerationLanguage"/>.
    /// </summary>
    public static void MapClientGenerationApis(this WebApplication app)
    {
        foreach (var generationLanguage in Enum.GetValues<GenerationLanguage>())
        {
            var route = $"/{generationLanguage}";

            app.MapApiClientEndpoint(route, genConfig =>
                {
                    genConfig.SwaggerDocumentName = "v1";
                    genConfig.Language = generationLanguage;
                    genConfig.ClientNamespaceName = "DotNetAtlas";
                    genConfig.ClientClassName = $"{generationLanguage}Client";
                },
                options =>
                {
                    options.CacheOutput(p => p.Expire(TimeSpan.FromDays(1)));
                    options.ExcludeFromDescription();
                });
        }
    }

    public static WebApplicationBuilder AddPresentation(this WebApplicationBuilder builder)
    {
        builder.Services.AddFastEndpoints(options =>
            {
                options.SourceGeneratorDiscoveredTypes.AddRange(DiscoveredTypes.All);
            })
            .AddAuthSwaggerDocument(builder.Configuration);

        builder.Services.AddCorsInternal(builder.Configuration);
        builder.Services.AddRazorPages();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        if (builder.Environment.IsProduction())
        {
            builder.Services.AddProblemDetails();
        }
        else
        {
            builder.Services.AddProblemDetailsWithExceptions();
        }

        return builder;
    }

    private static IServiceCollection AddCorsInternal(this IServiceCollection services, ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<CorsPolicyOptions>()
            .Bind(configuration.GetSection(CorsPolicyOptions.Section));
        var corsOptions =
            configuration.GetRequiredSection(CorsPolicyOptions.Section).Get<CorsPolicyOptions>()!;

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyOptions.DefaultCorsPolicyName, policy =>
            {
                if (corsOptions.AllowedOrigins.Contains("*"))
                {
                    policy.AllowAnyOrigin();
                }
                else
                {
                    policy.WithOrigins(corsOptions.AllowedOrigins);

                    if (corsOptions.AllowWildcardSubdomains)
                    {
                        policy.SetIsOriginAllowedToAllowWildcardSubdomains();
                    }
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
