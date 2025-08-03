using DotNetAtlas.Api.Common.Extensions;
using FastEndpoints.Swagger;
using OpenApiServer = NSwag.OpenApiServer;

namespace DotNetAtlas.Api.Common.Swagger
{
    public static class SwaggerDependencyInjection
    {
        public static IServiceCollection AddSwaggerDoc(
            this IServiceCollection services,
            WebApplicationBuilder builder)
        {
            services.SwaggerDocument(options =>
            {
                options.DocumentSettings = settings =>
                {
                    if (builder.Environment.IsOpenApiGenerationBuild())
                    {
                        settings.PostProcess = document =>
                        {
                            document.Servers.Add(new OpenApiServer
                            {
                                Url = "http://localhost:5159"
                            });
                        };
                    }

                    settings.DocumentName = "v1";
                };
                options.ShortSchemaNames = true;
                options.RemoveEmptyRequestSchema = true;
                options.EnableJWTBearerAuth = false;
            });

            return services;
        }
    }
}