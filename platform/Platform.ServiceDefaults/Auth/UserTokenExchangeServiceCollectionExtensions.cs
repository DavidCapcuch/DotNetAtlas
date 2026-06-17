using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.ServiceDefaults.Auth;

/// <summary>
/// DI + <see cref="IHttpClientBuilder"/> extensions for the buyer-scoped RFC 8693 token-exchange
/// outbound path (ADR-0010 amendment 2026-06-06). The companion of
/// <see cref="ServiceAuthServiceCollectionExtensions"/> (the non-buyer-scoped
/// <c>client_credentials</c> path) — both reuse the same <see cref="ServiceAuthOptions"/> identity and
/// the dedicated token-endpoint <see cref="HttpClient"/>, so <c>AddServiceAuth(serviceName)</c> must be
/// registered first.
/// </summary>
public static class UserTokenExchangeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="TokenExchangeHandler"/> and the <see cref="IHttpContextAccessor"/> it
    /// reads the inbound user JWT + buyer <c>sub</c> from. Pair with
    /// <see cref="ServiceAuthServiceCollectionExtensions.AddServiceAuth(IServiceCollection, string)"/>
    /// (which binds <see cref="ServiceAuthOptions"/> and the token-endpoint client) and with the
    /// <c>IHttpClientBuilder.AddUserTokenExchange(string)</c> extension on each buyer-scoped client.
    /// </summary>
    public static IServiceCollection AddUserTokenExchange(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddTransient<TokenExchangeHandler>();

        return services;
    }

    /// <summary>
    /// Attaches the <see cref="TokenExchangeHandler"/> to a named/typed <see cref="HttpClient"/> and pins
    /// the OAuth2 <paramref name="scope"/> (which drives the exchanged token's callee audience) for every
    /// outbound call on that client. Registered before the resilience handler so a resilience retry
    /// re-sends the request with the already-attached (cached) exchanged token (bff.md § 2.3).
    /// </summary>
    public static IHttpClientBuilder AddUserTokenExchange(this IHttpClientBuilder builder, string scope)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scope);

        return builder
            .AddHttpMessageHandler(() => new ExchangeScopePinningHandler(scope))
            .AddHttpMessageHandler<TokenExchangeHandler>();
    }

    /// <summary>
    /// Tiny inline handler that stamps the caller-supplied <c>scope</c> onto
    /// <see cref="HttpRequestMessage.Options"/> so the downstream <see cref="TokenExchangeHandler"/>
    /// reads it per-request.
    /// </summary>
    private sealed class ExchangeScopePinningHandler(string scope) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Options.Set(TokenExchangeHandler.ScopeRequestOptionKey, scope);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
