using DotNetAtlas.Api.Endpoints;
using FastEndpoints;

namespace DotNetAtlas.Api.Common;

internal static class FastEndpointsDependencyInjection
{
    internal static IServiceCollection AddFastEndpointsInternal(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddFastEndpoints(options =>
            {
                options.SourceGeneratorDiscoveredTypes.AddRange(DiscoveredTypes.All);
            })
            .AddAuthSwaggerDocument(configuration);

        return services;
    }

    internal static WebApplication UseFastEndpointsInternal(
        this WebApplication app)
    {
        app.UseFastEndpoints(config =>
            {
                config.Errors.UseProblemDetails(detailsConfig =>
                {
                    detailsConfig.IndicateErrorCode = true;
                    detailsConfig.IndicateErrorSeverity = false;
                });
                config.Endpoints.Filter = ep =>
                {
                    if (app.Environment.IsProduction() &&
                        ep.EndpointTags?.Contains(EndpointGroupConstants.Dev) is true)
                    {
                        return false;
                    }

                    return true;
                };

                config.Versioning.Prefix = "v";
                config.Versioning.PrependToRoute = true;
                config.Versioning.DefaultVersion = 1;
                config.Endpoints.RoutePrefix = "api";
                config.Binding.ReflectionCache
                    .AddFromDotNetAtlasApi();
            })
            .UseAuthSwaggerGen(app.Configuration);

        return app;
    }
}
