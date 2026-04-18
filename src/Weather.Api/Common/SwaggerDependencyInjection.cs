using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authorization;
using NSwag;
using NSwag.AspNetCore;
using Weather.Api.Common.Config;
using Weather.Api.Common.Swagger;
using Weather.Application.Common.Observability;
using Weather.Infrastructure.Common.Authentication;
using Weather.Infrastructure.Common.Authorization;
using OpenApiServer = NSwag.OpenApiServer;

namespace Weather.Api.Common;

internal static class SwaggerDependencyInjection
{
    public static IServiceCollection AddAuthSwaggerDocument(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.SwaggerDocument(options =>
        {
            options.DocumentSettings = settings =>
            {
                var openApiInfo = configuration
                    .GetRequiredSection(SwaggerConfigSections.OpenApiInfoSection)
                    .Get<OpenApiInfo>()!;

                settings.PostProcess = document =>
                {
                    document.Servers.Add(new OpenApiServer
                    {
                        Url = configuration[$"{SwaggerConfigSections.OpenApiInfoSection}:ServerUrl"]
                    });
                    document.Info = openApiInfo;
                };

                var documentName = configuration[$"{SwaggerConfigSections.OpenApiInfoSection}:DocumentName"]!;
                settings.DocumentName = documentName;
                settings.Title = openApiInfo.Title;
                settings.Version = openApiInfo.Version;

                settings.OperationProcessors.Add(
                    new AuthDescriptionOperationProcessor(
                        options.Services.GetRequiredService<IAuthorizationPolicyProvider>()));
                settings.DocumentProcessors.Add(new SignalRTypesDocumentProcessor());

                var authority = configuration[$"{AuthConfigSections.JwtBearerConfigSection}:Authority"]!;
                var tokenUrl = $"{authority}/protocol/openid-connect/token";
                var authorizationUrl = $"{authority}/protocol/openid-connect/auth";

                var scopes = AuthScopes.List.ToDictionary(s1 => s1.Name, s2 => s2.Description);
                settings.AddAuth(nameof(OpenApiSecuritySchemeType.OAuth2), new OpenApiSecurityScheme
                {
                    Type = OpenApiSecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = authorizationUrl,
                            TokenUrl = tokenUrl,
                            RefreshUrl = tokenUrl,
                            Scopes = scopes
                        }
                    },
                    Flow = OpenApiOAuth2Flow.AccessCode,
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

            // Swagger uses a dedicated public Keycloak client (see realm-export.json ->
            // "dotnetatlas-swagger") with PKCE. No ClientSecret is shipped to the browser
            // because the confidential backend client secret must never leak into JS bundles.
            var swaggerClientId = configuration[
                $"{AuthConfigSections.SwaggerClientConfigSection}:ClientId"]!;

            uiSettings.OAuth2Client = new OAuth2ClientSettings
            {
                AppName = $"{ApplicationInfo.AppName} Swagger Client",
                ClientId = swaggerClientId,
                UsePkceWithAuthorizationCodeGrant = true
            };

            foreach (var scope in AuthScopes.List)
            {
                uiSettings.OAuth2Client.Scopes.Add(scope.Name);
            }
        });

        return app;
    }
}
