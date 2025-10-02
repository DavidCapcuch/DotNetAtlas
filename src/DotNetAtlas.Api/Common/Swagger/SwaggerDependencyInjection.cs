using DotNetAtlas.Infrastructure.Common.Authentication;
using DotNetAtlas.Infrastructure.Common.Authorization;
using DotNetAtlas.Infrastructure.Common.Config;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using NSwag;
using NSwag.AspNetCore;
using OpenApiServer = NSwag.OpenApiServer;

namespace DotNetAtlas.Api.Common.Swagger;

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
                var tokenUrl = $"{authority}/oauth2/token";
                var authorizationUrl = $"{authority}/oauth2/authorize";

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

            var appName = configuration[$"{ApplicationOptions.Section}:AppName"];
            var oAuthConfig = configuration
                .GetRequiredSection(AuthConfigSections.OAuthConfigSection)
                .Get<OAuthOptions>()!;

            uiSettings.OAuth2Client = new OAuth2ClientSettings
            {
                AppName = $"{appName} Swagger Client",
                ClientId = oAuthConfig.ClientId,
                ClientSecret = oAuthConfig.ClientSecret,
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
