using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    /// <para>
    /// Paired with the <c>IHttpClientBuilder.AddServiceAuth(string)</c> extension on each outbound
    /// <see cref="HttpClient"/> that should carry a bearer token. The handler depends on
    /// <see cref="TimeProvider"/>, which <c>AddServiceDefaults</c> registers platform-wide
    /// (<see cref="TimeProvider.System"/>); tests override with <c>FakeTimeProvider</c> per ADR-0015.
    /// </para>
    /// <para>
    /// <b>Deployed guard</b> (<see cref="HostEnvironmentExtensions.IsDeployedEnvironment"/>): the
    /// <c>Authority</c> must be an <c>https://</c> URL. The token endpoint derived from it receives the
    /// client-credentials <c>client_secret</c> and RFC 8693 exchanged user tokens, so a plaintext
    /// <c>http://</c> Authority in a deployed host is a MITM surface on those secrets — the outbound
    /// symmetry of the inbound <see cref="JwtBearerConfigurator"/> guard. The check rides the existing
    /// <c>ValidateOnStart</c> chain, so a misconfigured deployed host <b>fails to boot</b> (ADR-0009
    /// item 10) rather than leaking the secret on the first outbound call. No-op in Development /
    /// Testing, which run against a local http Keycloak.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddServiceAuth(this IServiceCollection services, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        services.AddOptionsWithValidateOnStart<ServiceAuthOptions>()
            .BindConfiguration(ServiceAuthOptions.Section)
            .ValidateDataAnnotations()
            .Validate<IHostEnvironment>(
                (opts, environment) => !environment.IsDeployedEnvironment() || IsHttpsAuthority(opts.Authority),
                DeployedHttpsAuthorityFailureMessage)
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

    private const string DeployedHttpsAuthorityFailureMessage =
        "Outbound service-to-service auth must use an https:// 'ServiceAuth:Authority' in deployed " +
        "environments so the client-credentials client_secret and RFC 8693 exchanged user tokens are " +
        "POSTed to the Keycloak token endpoint over TLS. Point 'ServiceAuth:Authority' at an https:// " +
        "OIDC realm URL. See ADR-0009 'Taking this to production'.";

    // Absolute-URI parse (not a StartsWith) so a malformed Authority also fails closed in deployed envs;
    // Uri normalizes the scheme to lower case, so an "HTTPS://" Authority still matches.
    private static bool IsHttpsAuthority(string authority) =>
        Uri.TryCreate(authority, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

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
