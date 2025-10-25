using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DotNetAtlas.Infrastructure.Common.Authentication;
using DotNetAtlas.Infrastructure.Common.Authorization;
using DotNetAtlas.Infrastructure.Common.Config;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.Infrastructure.Common;

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
    /// <param name="isClusterEnvironment">Whether running in a cluster environment.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAuthenticationInternal(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isClusterEnvironment)
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
                    var path = ctx.Request.Path;
                    var hasAuthHeader = ctx.Request.Headers.ContainsKey("Authorization");
                    if (hasAuthHeader ||
                        path.StartsWithSegments(InfrastructureConstants.ApiBasePath,
                            StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWithSegments(InfrastructureConstants.HubsBasePath,
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
                if (isClusterEnvironment)
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
                        if (context.HttpContext.Request.Path.StartsWithSegments(InfrastructureConstants.HubsBasePath,
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
                options.Cookie.IsEssential = true;
                options.Cookie.HttpOnly = true;

                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments(InfrastructureConstants.ApiBasePath,
                                StringComparison.OrdinalIgnoreCase) ||
                            ctx.Request.Path.StartsWithSegments(InfrastructureConstants.HubsBasePath,
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
                            var handler = new JwtSecurityTokenHandler();

                            if (!handler.CanReadToken(context.TokenEndpointResponse.AccessToken))
                            {
                                return Task.CompletedTask;
                            }

                            var jwtAccessToken = handler.ReadJwtToken(context.TokenEndpointResponse.AccessToken);
                            if (jwtAccessToken == null)
                            {
                                return Task.CompletedTask;
                            }

                            var roles = jwtAccessToken.Claims
                                .Where(c => c.Type is "roles" or "role")
                                .Select(c => c.Value)
                                .ToList();
                            var roleClaims = roles.Select(r => new Claim(ClaimTypes.Role, r));

                            var identity = new ClaimsIdentity(roleClaims);
                            context.Principal?.AddIdentity(identity);

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

                if (isClusterEnvironment)
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
