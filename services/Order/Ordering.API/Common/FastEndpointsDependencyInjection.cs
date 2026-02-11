using FastEndpoints;
using FastEndpoints.Swagger;
using NSwag;
using Ordering.API.Common.Config;

namespace Ordering.API.Common;

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
            .SwaggerDocument(options =>
            {
                options.DocumentSettings = settings =>
                {
                    var openApiInfo = configuration
                        .GetRequiredSection(SwaggerConfigSections.OpenApiInfoSection)
                        .Get<OpenApiInfo>()!;

                    var documentName = configuration[$"{SwaggerConfigSections.OpenApiInfoSection}:DocumentName"]!;
                    settings.DocumentName = documentName;
                    settings.Title = openApiInfo.Title;
                    settings.Version = openApiInfo.Version;

                    options.ShortSchemaNames = true;
                    options.RemoveEmptyRequestSchema = true;
                    options.EnableJWTBearerAuth = false;
                };
            });

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

            config.Versioning.Prefix = "v";
            config.Versioning.PrependToRoute = true;
            config.Versioning.DefaultVersion = 1;
            config.Endpoints.RoutePrefix = "api";
            config.Binding.ReflectionCache
                .AddFromOrderingAPI();
        });

        return app;
    }
}
