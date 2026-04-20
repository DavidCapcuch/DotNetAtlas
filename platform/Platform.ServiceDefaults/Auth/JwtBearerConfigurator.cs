using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Platform.ServiceDefaults.Auth;

/// <summary>
/// Inbound-side helper for validating OAuth2 bearer tokens (ADR-0010). Wires an
/// <see cref="AuthenticationBuilder"/> with defaults derived from
/// <see cref="ServiceAuthOptions"/>; callers override via the optional configure delegate.
/// </summary>
public static class JwtBearerConfigurator
{
    /// <summary>
    /// Registers authentication and a JWT-bearer scheme with:
    /// <list type="bullet">
    /// <item><description><c>Authority</c> = <see cref="ServiceAuthOptions.Authority"/></description></item>
    /// <item><description><c>Audience</c> = <see cref="ServiceAuthOptions.ServiceName"/></description></item>
    /// <item><description><c>ValidateIssuer</c> / <c>ValidateAudience</c> / <c>ValidateLifetime</c> = <c>true</c></description></item>
    /// <item><description><c>ClockSkew</c> = 5 minutes per ADR-0010</description></item>
    /// </list>
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="configure">Optional callback to override any JwtBearerOption.</param>
    public static AuthenticationBuilder AddPlatformJwtBearer(
        this IServiceCollection services,
        Action<JwtBearerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<ServiceAuthOptions>>((jwt, serviceAuth) =>
            {
                var opts = serviceAuth.Value;
                jwt.Authority = opts.Authority;
                jwt.Audience = opts.ServiceName;
                jwt.RequireHttpsMetadata = !string.Equals(
                    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                    "Development",
                    StringComparison.OrdinalIgnoreCase);
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(5),
                    ValidAudience = opts.ServiceName,
                    ValidIssuer = opts.Authority,
                };

                configure?.Invoke(jwt);
            });

        return builder;
    }
}
