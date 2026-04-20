using Microsoft.Extensions.DependencyInjection;

namespace Platform.ServiceDefaults.Auth;

/// <summary>
/// DI + <see cref="IHttpClientBuilder"/> extensions for OAuth2 service-to-service auth (ADR-0010).
/// </summary>
public static class ServiceAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ServiceAuthOptions"/> (bound to configuration section
    /// <c>ServiceAuth</c>), the dedicated token-endpoint <see cref="HttpClient"/>, and the
    /// <see cref="ClientCredentialsTokenHandler"/>.
    /// </summary>
    /// <remarks>
    /// Paired with the <c>IHttpClientBuilder.AddServiceAuth(string)</c> extension on each outbound
    /// <see cref="HttpClient"/> that should carry a bearer token. <c>IClock</c> must be registered
    /// separately (Wave 0 M1 — <c>AddSharedKernel()</c>).
    /// </remarks>
    public static IServiceCollection AddServiceAuth(this IServiceCollection services, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        services.AddOptionsWithValidateOnStart<ServiceAuthOptions>()
            .BindConfiguration(ServiceAuthOptions.Section)
            .ValidateDataAnnotations()
            .PostConfigure(opts =>
            {
                if (string.IsNullOrWhiteSpace(opts.ServiceName))
                {
                    opts.ServiceName = serviceName;
                }
            });

        // Token-endpoint client must not have the bearer handler attached (would recurse).
        services.AddHttpClient(ServiceAuthOptions.TokenEndpointHttpClientName);

        services.AddTransient<ClientCredentialsTokenHandler>();

        return services;
    }

    /// <summary>
    /// Attaches the <see cref="ClientCredentialsTokenHandler"/> to a named/typed
    /// <see cref="HttpClient"/> and pins the OAuth2 <paramref name="scope"/> request-option for
    /// every outbound call on that client.
    /// </summary>
    public static IHttpClientBuilder AddServiceAuth(this IHttpClientBuilder builder, string scope)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scope);

        return builder
            .AddHttpMessageHandler(() => new ScopePinningHandler(scope))
            .AddHttpMessageHandler<ClientCredentialsTokenHandler>();
    }

    /// <summary>
    /// Tiny inline handler that stamps the caller-supplied <c>scope</c> onto
    /// <see cref="HttpRequestMessage.Options"/> so the downstream
    /// <see cref="ClientCredentialsTokenHandler"/> reads it per-request.
    /// </summary>
    private sealed class ScopePinningHandler(string scope) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Options.Set(ClientCredentialsTokenHandler.ScopeRequestOptionKey, scope);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
