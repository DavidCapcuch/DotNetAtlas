using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Weather.Infrastructure.Common.Authentication;
using Weather.Infrastructure.Common.Authorization;
using Weather.Infrastructure.Common.Constants;

namespace Weather.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for authentication and authorization infrastructure.
/// Configures JWT Bearer, Cookie, and OpenID Connect authentication schemes.
/// </summary>
public static class AuthDependencyInjection
{
    /// <summary>
    /// Configures authentication schemes (JWT Bearer, Cookie, OpenID Connect).
    /// Sets up policy scheme for flexible authentication method selection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <param name="isDeployedEnvironment">Whether running in a deployed environment.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAuthenticationInternal(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isDeployedEnvironment)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = AuthPolicySchemes.JwtOrCookie;
                options.DefaultAuthenticateScheme = AuthPolicySchemes.JwtOrCookie;
                options.DefaultChallengeScheme = AuthPolicySchemes.JwtOrCookie;
            })
            .AddPolicyScheme(AuthPolicySchemes.JwtOrCookie, AuthPolicySchemes.JwtOrCookie, options =>
            {
                options.ForwardDefaultSelector = ctx =>
                {
                    // Route strictly by path. Earlier revisions also routed to JWT on any
                    // Authorization header; that let a bearer token sneak into UI paths
                    // that were designed to be cookie-gated. API and Hub paths use JWT;
                    // everything else falls back to the Cookie scheme.
                    var path = ctx.Request.Path;
                    if (path.StartsWithSegments(BasePaths.ApiBasePath,
                            StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWithSegments(BasePaths.HubsBasePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return JwtBearerDefaults.AuthenticationScheme;
                    }

                    return CookieAuthenticationDefaults.AuthenticationScheme;
                };
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                configuration.Bind(AuthConfigSections.JwtBearerConfigSection, options);
                // Role claims: JsonWebTokenHandler with MapInboundClaims=true (default) remaps
                // Keycloak's "roles" array claim to ClaimTypes.Role, which matches the default
                // TokenValidationParameters.RoleClaimType. No override needed.
                if (isDeployedEnvironment)
                {
                    options.RequireHttpsMetadata = true;
                }

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // For SignalR auth. Sending the access token in the query string is required
                        // when using WebSockets or ServerSentEvents due to a limitation in Browser APIs.
                        // See https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-9.0
                        if (context.HttpContext.Request.Path.StartsWithSegments(BasePaths.HubsBasePath,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            var accessToken = context.Request.Query["access_token"];
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                configuration.Bind(AuthConfigSections.CookieConfigSection, options);
                options.Cookie.Name = "Weather.Auth";
                options.Cookie.IsEssential = true;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = isDeployedEnvironment
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;

                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments(BasePaths.ApiBasePath,
                                StringComparison.OrdinalIgnoreCase) ||
                            ctx.Request.Path.StartsWithSegments(BasePaths.HubsBasePath,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }

                        ctx.Response.Redirect(ctx.RedirectUri);
                        return Task.CompletedTask;
                    }
                };
            })
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                configuration.Bind(AuthConfigSections.OidcConfigSection, options);
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                foreach (var scope in AuthScopes.List)
                {
                    options.Scope.Add(scope.Name);
                }

                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (!string.IsNullOrWhiteSpace(context.TokenEndpointResponse?.AccessToken))
                        {
                            var handler = new JsonWebTokenHandler();

                            if (!handler.CanReadToken(context.TokenEndpointResponse.AccessToken))
                            {
                                return Task.CompletedTask;
                            }

                            var jwtAccessToken = handler.ReadJsonWebToken(context.TokenEndpointResponse.AccessToken);
                            if (jwtAccessToken == null)
                            {
                                return Task.CompletedTask;
                            }

                            if (context.Principal is null)
                            {
                                // Defensive: OIDC middleware always sets Principal before
                                // OnTokenValidated fires. A null here means something
                                // upstream is misconfigured - fail loud rather than silently
                                // drop role claims.
                                var logger = context.HttpContext.RequestServices
                                    .GetRequiredService<ILoggerFactory>()
                                    .CreateLogger("OpenIdConnectEvents");
                                logger.LogWarning(
                                    "OIDC token validated without a Principal; role claims will not be added.");
                                return Task.CompletedTask;
                            }

                            var roles = jwtAccessToken.Claims
                                .Where(c => c.Type is "roles" or "role")
                                .Select(c => c.Value)
                                .ToList();
                            var roleClaims = roles.Select(r => new Claim(ClaimTypes.Role, r));

                            var identity = new ClaimsIdentity(roleClaims);
                            context.Principal.AddIdentity(identity);

                            var expiration = jwtAccessToken.ValidTo;
                            if (context.Properties is not null)
                            {
                                context.Properties.ExpiresUtc = expiration;
                                context.Properties.IsPersistent = true;
                            }
                        }

                        return Task.CompletedTask;
                    }
                };

                if (isDeployedEnvironment)
                {
                    options.RequireHttpsMetadata = true;
                }
            });
        services.AddHttpContextAccessor();

        return services;
    }

    /// <summary>
    /// Configures authorization policies for the application.
    /// Defines role-based access control policies.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAuthorizationInternal(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.DevOnly, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(Roles.Developer);
            });

        return services;
    }
}
