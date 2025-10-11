using DotNetAtlas.Api.Common.Config;
using DotNetAtlas.Api.Common.Exceptions;
using DotNetAtlas.Api.Common.Swagger;
using DotNetAtlas.Api.SignalR.WeatherAlerts;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Infrastructure.Common.Config;
using FastEndpoints;
using FastEndpoints.ClientGen.Kiota;
using Kiota.Builder;
using TypedSignalR.Client.DevTools;

namespace DotNetAtlas.Api.Common;

public static class ApiDependencyInjection
{
    /// <summary>
    /// Maps client generation APIs for each supported <see cref="GenerationLanguage"/>.
    /// </summary>
    public static void MapClientGenerationApis(this WebApplication app)
    {
        var documentName = app.Configuration[
            $"{SwaggerConfigSections.OpenApiInfoSection}:DocumentName"]!;

        foreach (var generationLanguage in Enum.GetValues<GenerationLanguage>())
        {
            var route = $"/{generationLanguage}";

            app.MapApiClientEndpoint(route, genConfig =>
                {
                    genConfig.SwaggerDocumentName = documentName;
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

    public static void MapSignalRHubsInternal(this WebApplication app)
    {
        if (!app.Environment.IsProduction())
        {
            app.UseSignalRHubDevelopmentUI();
        }

        app.UseSignalRHubSpecification();
        app.MapHub<WeatherAlertHub>(WeatherAlertHub.RoutePattern);
    }

    public static IServiceCollection AddPresentation(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddFastEndpoints(options =>
            {
                options.SourceGeneratorDiscoveredTypes.AddRange(DiscoveredTypes.All);
            })
            .AddAuthSwaggerDocument(configuration);

        services.AddCorsInternal(configuration);
        services.AddRazorPages();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.AddSignalR();
        services.AddScoped<IWeatherAlertNotifier, WeatherAlertNotifier>();

        return services;
    }

    private static IServiceCollection AddCorsInternal(
        this IServiceCollection services,
        ConfigurationManager configuration)
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
