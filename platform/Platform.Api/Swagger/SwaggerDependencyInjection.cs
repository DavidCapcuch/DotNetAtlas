using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSwag;
using NSwag.AspNetCore;

namespace Platform.Api.Swagger;

/// <summary>
/// Shared Swagger wiring for the bounded-context APIs. Registers an OpenAPI document whose
/// "Authorize" button drives the Keycloak OAuth2 Authorization-Code + PKCE flow so that
/// "Try it out" requests carry a real bearer token. Uses the public <c>dotnetatlas-swagger</c>
/// Keycloak client (see <c>src/keycloak/realm-export.json</c>); no client secret reaches the browser.
/// </summary>
public static class SwaggerDependencyInjection
{
    private static readonly IReadOnlyDictionary<string, string> DefaultScopes =
        new Dictionary<string, string>
        {
            ["openid"] = "OpenID.",
            ["profile"] = "Profile.",
            ["email"] = "Email.",
            ["offline_access"] = "Generate refresh token.",
        };

    /// <summary>
    /// Registers a FastEndpoints Swagger document configured for the Keycloak OAuth2
    /// Authorization-Code + PKCE flow. The token and authorization URLs are derived from
    /// <c>Authentication:JwtBearer:Authority</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">App configuration; the JwtBearer authority is read from it.</param>
    /// <param name="title">OpenAPI document title, e.g. <c>"Basket API"</c>.</param>
    /// <param name="version">OpenAPI document version, e.g. <c>"v1"</c>.</param>
    /// <param name="description">OpenAPI document description.</param>
    /// <param name="scopes">
    /// Optional scope-name to description map advertised by the OAuth2 flow. Defaults to the standard
    /// OIDC scopes (<c>openid</c>, <c>profile</c>, <c>email</c>, <c>offline_access</c>).
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddPlatformAuthSwaggerDocument(
        this IServiceCollection services,
        IConfiguration configuration,
        string title,
        string version,
        string description,
        IReadOnlyDictionary<string, string>? scopes = null)
    {
        scopes ??= DefaultScopes;

        services.SwaggerDocument(options =>
        {
            // ADR-0012: endpoints declare Version(1); without an explicit cap FastEndpoints
            // excludes all versioned endpoints from the document (default 0), leaving paths empty.
            options.MaxEndpointVersion = 1;
            options.ShortSchemaNames = true;
            options.RemoveEmptyRequestSchema = true;
            options.EnableJWTBearerAuth = false;
            options.DocumentSettings = settings =>
            {
                settings.Title = title;
                settings.Version = version;
                settings.Description = description;

                settings.OperationProcessors.Add(
                    new AuthDescriptionOperationProcessor(
                        options.Services.GetRequiredService<IAuthorizationPolicyProvider>()));

                var authority = configuration[$"{SwaggerConfigSections.JwtBearerConfigSection}:Authority"]!;
                var tokenUrl = $"{authority}/protocol/openid-connect/token";
                var authorizationUrl = $"{authority}/protocol/openid-connect/auth";

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
                            Scopes = scopes.ToDictionary(s => s.Key, s => s.Value),
                        },
                    },
                    Flow = OpenApiOAuth2Flow.AccessCode,
                    Description = @"IMPORTANT NOTE: If you do not specify any scope in the authentication request
                        then generated access token gets all scopes the specified client_id is authorized for.",
                });
            };
        });

        return services;
    }

    /// <summary>
    /// Serves the Swagger UI with the Keycloak OAuth2 + PKCE client pre-configured. The public
    /// Swagger client id is read from <c>Authentication:SwaggerClient:ClientId</c>; no secret is
    /// shipped to the browser.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configuration">App configuration; the Swagger client id is read from it.</param>
    /// <returns>The same <paramref name="app"/> instance for chaining.</returns>
    public static IApplicationBuilder UsePlatformAuthSwaggerGen(
        this IApplicationBuilder app,
        IConfiguration configuration)
    {
        app.UseSwaggerGen(null, uiSettings =>
        {
            uiSettings.ConfigureDefaults();
            uiSettings.DocExpansion = "list";

            // Swagger uses a dedicated public Keycloak client (see realm-export.json ->
            // "dotnetatlas-swagger") with PKCE. No ClientSecret is shipped to the browser
            // because the confidential service-client secret must never leak into JS bundles.
            var swaggerClientId = configuration[
                $"{SwaggerConfigSections.SwaggerClientConfigSection}:ClientId"]!;

            uiSettings.OAuth2Client = new OAuth2ClientSettings
            {
                AppName = "DotNet Atlas Swagger Client",
                ClientId = swaggerClientId,
                UsePkceWithAuthorizationCodeGrant = true,
            };

            foreach (var scope in DefaultScopes.Keys)
            {
                uiSettings.OAuth2Client.Scopes.Add(scope);
            }
        });

        return app;
    }
}
