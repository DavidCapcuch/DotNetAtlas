using DotNetAtlas.Api.Common.Config;
using DotNetAtlas.Api.Common.Exceptions;
using DotNetAtlas.Api.SignalRHubs.WeatherAlerts;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Infrastructure.Common.Authorization;
using DotNetAtlas.Infrastructure.Common.Config;
using FastEndpoints.ClientGen.Kiota;
using HealthChecks.UI.Client;
using Kiota.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TypedSignalR.Client.DevTools;

namespace DotNetAtlas.Api.Common;

public static class ApiDependencyInjection
{
    public static IServiceCollection AddPresentation(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddFastEndpointsInternal(configuration);

        services.AddCorsInternal(configuration);

        services.AddRazorPages();

        services
            .AddProblemDetails()
            .AddExceptionHandler<GlobalExceptionHandler>();

        services.AddScoped<IWeatherAlertNotifier, WeatherAlertNotifier>();

        return services;
    }

    private static IServiceCollection AddCorsInternal(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<CorsPolicyOptions>()
            .BindConfiguration(CorsPolicyOptions.Section)
            .ValidateDataAnnotations();

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

    /// <summary>
    /// Maps client generation APIs for each supported <see cref="GenerationLanguage"/>.
    /// </summary>
    public static WebApplication MapClientGenerationApis(this WebApplication app)
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

        return app;
    }

    public static WebApplication MapHealthChecksInternal(this WebApplication app)
    {
        app.MapHealthChecks(InfrastructureConstants.ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(InfrastructureConstants.ReadinessTag),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).ShortCircuit();

        app.MapHealthChecks(InfrastructureConstants.HealthEndpointPath, new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(InfrastructureConstants.LivenessTag),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).ShortCircuit();

        app.MapHealthChecksUI()
            .RequireAuthorization(AuthPolicies.DevOnly);

        return app;
    }

    public static WebApplication MapSignalRWithDevTools(this WebApplication app)
    {
        if (!app.Environment.IsProduction())
        {
            app.UseSignalRHubDevelopmentUI();
        }

        app.UseSignalRHubSpecification();
        app.MapHub<WeatherAlertHub>(WeatherAlertHub.RoutePattern);

        return app;
    }
}
