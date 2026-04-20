using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.SharedKernel.Time;

namespace Platform.ServiceDefaults.Auth;

/// <summary>
/// <see cref="DelegatingHandler"/> that attaches a cached Keycloak client-credentials bearer
/// token to every outbound request (ADR-0010). On a <c>401 Unauthorized</c> it invalidates the
/// cache entry and retries once to handle signing-key rotation edges.
/// </summary>
/// <remarks>
/// <para>
/// Cache key = (<see cref="ServiceAuthOptions.ServiceName"/>, scope). Concurrent callers to the
/// same key share a single in-flight token fetch via the <see cref="Task{TResult}"/> stored in the
/// dictionary. Expiry check uses <see cref="IClock.UtcNow"/> + the 30-second buffer per ADR-0010.
/// </para>
/// <para>
/// The per-request scope is passed via <see cref="HttpRequestMessage.Options"/> under
/// <see cref="ScopeRequestOptionKey"/>; the companion
/// <c>IHttpClientBuilder.AddServiceAuth(string)</c> extension sets this for you.
/// </para>
/// <para>
/// Token acquisition uses a distinct named <see cref="HttpClient"/>
/// (<see cref="ServiceAuthOptions.TokenEndpointHttpClientName"/>) so the handler never calls
/// itself recursively.
/// </para>
/// </remarks>
public sealed class ClientCredentialsTokenHandler : DelegatingHandler
{
    /// <summary>
    /// <see cref="HttpRequestMessage.Options"/> key used to carry the per-request OAuth2 scope.
    /// </summary>
    public static readonly HttpRequestOptionsKey<string> ScopeRequestOptionKey = new("ServiceAuth.Scope");

    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(30);

    // Lazy<Task<T>> is the single-flight pattern — ConcurrentDictionary.GetOrAdd may run its
    // value factory multiple times under concurrency, but Lazy with ExecutionAndPublication
    // guarantees the inner Task<CachedToken> is produced exactly once per cache entry.
    private readonly ConcurrentDictionary<(string ServiceName, string Scope), Lazy<Task<CachedToken>>> _cache = new();

    private readonly IOptionsMonitor<ServiceAuthOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClock _clock;
    private readonly ILogger<ClientCredentialsTokenHandler> _logger;

    /// <summary>Creates a new handler. Typically resolved from DI.</summary>
    public ClientCredentialsTokenHandler(
        IOptionsMonitor<ServiceAuthOptions> options,
        IHttpClientFactory httpClientFactory,
        IClock clock,
        ILogger<ClientCredentialsTokenHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _httpClientFactory = httpClientFactory;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.CurrentValue;
        var scope = ResolveScope(request);
        var key = (options.ServiceName, scope);

        var token = await GetOrFetchTokenAsync(key, options, scope, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // 401 — invalidate and retry once (handles signing-key rotation edge). Dispose the first
        // response so its underlying socket is returned to the pool.
        response.Dispose();
        InvalidateCacheEntry(key);

        token = await GetOrFetchTokenAsync(key, options, scope, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CachedToken> GetOrFetchTokenAsync(
        (string ServiceName, string Scope) key,
        ServiceAuthOptions options,
        string scope,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            // Lazy<Task<>> + ExecutionAndPublication = one fetch per key, regardless of how many
            // concurrent callers hit GetOrAdd. The shared fetch uses CancellationToken.None so
            // the first caller's cancellation does not kill every awaiter; each awaiter cancels
            // its own await via WaitAsync below.
            var lazy = _cache.GetOrAdd(key, _ => new Lazy<Task<CachedToken>>(
                () => FetchTokenAsync(options, scope, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

            CachedToken token;
            try
            {
                token = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller was cancelled — leave the shared fetch alone for other waiters.
                throw;
            }
            catch
            {
                // Shared fetch faulted — evict exactly this entry so the next caller retries.
                _cache.TryRemove(new KeyValuePair<(string, string), Lazy<Task<CachedToken>>>(key, lazy));
                throw;
            }

            if (_clock.UtcNow + ExpiryBuffer < token.ExpiresAt)
            {
                return token;
            }

            // Expiring — evict this exact Lazy (atomic; avoids racing a concurrent refresher).
            _cache.TryRemove(new KeyValuePair<(string, string), Lazy<Task<CachedToken>>>(key, lazy));
        }
    }

    private void InvalidateCacheEntry((string ServiceName, string Scope) key) => _cache.TryRemove(key, out _);

    private async Task<CachedToken> FetchTokenAsync(
        ServiceAuthOptions options,
        string scope,
        CancellationToken cancellationToken)
    {
        var tokenEndpoint = new Uri($"{options.Authority.TrimEnd('/')}/protocol/openid-connect/token");

        using var http = _httpClientFactory.CreateClient(ServiceAuthOptions.TokenEndpointHttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", options.ClientId),
            new("client_secret", options.ClientSecret),
        };
        if (!string.IsNullOrWhiteSpace(scope))
        {
            form.Add(new KeyValuePair<string, string>("scope", scope));
        }
        request.Content = new FormUrlEncodedContent(form);

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<TokenResponsePayload>(
            stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (payload is null || string.IsNullOrEmpty(payload.AccessToken) || payload.ExpiresIn <= 0)
        {
            throw new InvalidOperationException(
                "Keycloak token endpoint returned an empty or malformed response.");
        }

        _logger.LogDebug(
            "Fetched client-credentials token for {ServiceName} scope='{Scope}' expires_in={ExpiresIn}s",
            options.ServiceName, scope, payload.ExpiresIn);

        return new CachedToken(payload.AccessToken, _clock.UtcNow + TimeSpan.FromSeconds(payload.ExpiresIn));
    }

    private static string ResolveScope(HttpRequestMessage request) =>
        request.Options.TryGetValue(ScopeRequestOptionKey, out var scope) ? scope : string.Empty;

    private sealed record TokenResponsePayload(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
