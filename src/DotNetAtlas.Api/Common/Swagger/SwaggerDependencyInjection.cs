using DotNetAtlas.Api.Common.Authentication;
using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Infrastructure.Common.Config;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using NSwag;
using NSwag.AspNetCore;
using OpenApiServer = NSwag.OpenApiServer;

namespace DotNetAtlas.Api.Common.Swagger
{
    public static class SwaggerDependencyInjection
    {
        public static IServiceCollection AddAuthSwaggerDocument(
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

                    settings.DocumentName = "Release 1 2025-08-04";
                    settings.Title = "DotNetAtlas API";
                    settings.Version = "v1";

                    settings.OperationProcessors.Add(
                        new AuthDescriptionOperationProcessor(
                            options.Services.GetRequiredService<IAuthorizationPolicyProvider>()));

                    var openApiInfo = builder.Configuration
                        .GetRequiredSection(AuthConfigSections.Full.OPEN_API_INFO)
                        .Get<OpenApiInfo>()!;
                    settings.PostProcess = document =>
                    {
                        document.Info = openApiInfo;
                    };

                    var jwtBearerOptions = builder.Configuration
                        .GetRequiredSection(AuthConfigSections.Full.JWT_BEARER)
                        .Get<JwtBearerOptions>()!;

                    var tokenUrl = $"{jwtBearerOptions.Authority}/oauth2/token";
                    var authorizationUrl = $"{jwtBearerOptions.Authority}/oauth2/authorize";

                    var scopes = Scopes.List.ToDictionary(s1 => s1.Name, s2 => s2.Description);
                    settings.AddAuth(SecuritySchemes.O_AUTH2, new OpenApiSecurityScheme
                    {
                        Type = OpenApiSecuritySchemeType.OAuth2,
                        Flows = new OpenApiOAuthFlows
                        {
                            AuthorizationCode = new OpenApiOAuthFlow
                            {
                                AuthorizationUrl = authorizationUrl,
                                TokenUrl = tokenUrl,
                                Scopes = scopes
                            }
                        },
                        Description = @"IMPORTANT NOTE: If you do not specify any scope in the authentication request
                        then generated access token gets all scopes the specified client_id is authorized for."
                    });
                };
                options.MaxEndpointVersion = 1;
                options.ShortSchemaNames = true;
                options.RemoveEmptyRequestSchema = true;
                options.EnableJWTBearerAuth = false;
            });

            return services;
        }

        public static IApplicationBuilder UseAuthSwaggerGen(
            this IApplicationBuilder app,
            IConfiguration configuration)
        {
            app.UseSwaggerGen(null, uiSettings =>
            {
                uiSettings.ConfigureDefaults();
                uiSettings.DocExpansion = "list";

                var appName = configuration[$"{ApplicationOptions.SECTION}:AppName"];
                var oAuthConfig = configuration
                    .GetRequiredSection(AuthConfigSections.Full.O_AUTH_CONFIG)
                    .Get<OAuthOptions>()!;

                uiSettings.OAuth2Client = new OAuth2ClientSettings
                {
                    AppName = $"{appName} Swagger Client",
                    ClientId = oAuthConfig.ClientId,
                    ClientSecret = oAuthConfig.ClientSecret,
                    UsePkceWithAuthorizationCodeGrant = true
                };

                foreach (var scope in Scopes.List)
                {
                    uiSettings.OAuth2Client.Scopes.Add(scope.Name);
                }
            });

            return app;
        }
    }
}