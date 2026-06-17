using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Platform.ServiceDefaults.Auth;

/// <summary>
/// <see cref="DelegatingHandler"/> that attaches a cached <b>RFC 8693 token-exchange</b> bearer token
/// to every outbound request for a <b>buyer-scoped</b> callee (ADR-0010 amendment 2026-06-06). The
/// inbound user JWT is exchanged — via Keycloak <b>Standard Token Exchange</b> — for a token
/// re-audienced to the callee through the requested scope's <c>oidc-audience-mapper</c>, while
/// <b>preserving the buyer <c>sub</c></b>. On a <c>401 Unauthorized</c> it invalidates the cache
/// entry and retries once.
/// </summary>
/// <remarks>
/// <para>
/// Cache key = (<c>sub</c>, scope). The <c>sub</c> partition is load-bearing: a cached exchanged token
/// is <b>never</b> served to a different user, because the callee derives the resource owner from
/// <c>sub</c> (Basket <c>GetUserIdFromSubClaim</c>; Ordering / Invoicing buyer-self). Concurrent callers
/// for the same key share a single in-flight exchange via the <see cref="Lazy{T}"/>+<see cref="Task{TResult}"/>.
/// Expiry uses <see cref="TimeProvider.GetUtcNow"/> + a 30-second buffer (ADR-0010).
/// </para>
/// <para>
/// The subject token (the inbound user JWT) and the buyer <c>sub</c> are read from the current
/// <see cref="HttpContext"/>. Token exchange only applies to per-user buyer-scoped calls, which always
/// run inside an authenticated request; absence of a context / bearer / <c>sub</c> is a misconfiguration
/// and throws.
/// </para>
/// <para>
/// Mirrors <see cref="ClientCredentialsTokenHandler"/> (the non-buyer-scoped service-token path) but is
/// per-user. The per-request scope rides <see cref="HttpRequestMessage.Options"/> under
/// <see cref="ScopeRequestOptionKey"/>; the companion <c>IHttpClientBuilder.AddUserTokenExchange(string)</c>
/// extension sets it. Acquisition uses the shared token-endpoint <see cref="HttpClient"/>
/// (<see cref="ServiceAuthOptions.TokenEndpointHttpClientName"/>) so the handler never recurses.
/// </para>
/// </remarks>
public sealed class TokenExchangeHandler : DelegatingHandler
{
    /// <summary>
    /// <see cref="HttpRequestMessage.Options"/> key used to carry the per-request OAuth2 scope
    /// (which drives the exchanged token's callee audience).
    /// </summary>
    public static readonly HttpRequestOptionsKey<string> ScopeRequestOptionKey = new("UserTokenExchange.Scope");

    private const string TokenExchangeGrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(30);

    // Lazy<Task<T>> + ExecutionAndPublication = one exchange per (sub, scope) under concurrency
    // (see ClientCredentialsTokenHandler for the single-flight rationale).
    private readonly ConcurrentDictionary<(string Sub, string Scope), Lazy<Task<CachedToken>>> _cache = new();

    private readonly IOptionsMonitor<ServiceAuthOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TokenExchangeHandler> _logger;

    /// <summary>Creates a new handler. Typically resolved from DI.</summary>
    public TokenExchangeHandler(
        IOptionsMonitor<ServiceAuthOptions> options,
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        ILogger<TokenExchangeHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
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
        var (subjectToken, sub) = ResolveUser();
        var key = (sub, scope);

        var token = await GetOrFetchTokenAsync(key, options, scope, subjectToken, cancellationToken).ConfigureAwait(false);
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

        token = await GetOrFetchTokenAsync(key, options, scope, subjectToken, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CachedToken> GetOrFetchTokenAsync(
        (string Sub, string Scope) key,
        ServiceAuthOptions options,
        string scope,
        string subjectToken,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var lazy = _cache.GetOrAdd(key, _ => new Lazy<Task<CachedToken>>(
                () => FetchTokenAsync(options, scope, subjectToken, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

            CachedToken token;
            try
            {
                token = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Shared exchange faulted — evict exactly this entry so the next caller retries.
                _cache.TryRemove(new KeyValuePair<(string, string), Lazy<Task<CachedToken>>>(key, lazy));
                throw;
            }

            if (_timeProvider.GetUtcNow() + ExpiryBuffer < token.ExpiresAt)
            {
                return token;
            }

            _cache.TryRemove(new KeyValuePair<(string, string), Lazy<Task<CachedToken>>>(key, lazy));
        }
    }

    private void InvalidateCacheEntry((string Sub, string Scope) key) => _cache.TryRemove(key, out _);

    private async Task<CachedToken> FetchTokenAsync(
        ServiceAuthOptions options,
        string scope,
        string subjectToken,
        CancellationToken cancellationToken)
    {
        var tokenEndpoint = new Uri($"{options.Authority.TrimEnd('/')}/protocol/openid-connect/token");

        using var http = _httpClientFactory.CreateClient(ServiceAuthOptions.TokenEndpointHttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", TokenExchangeGrantType),
            new("client_id", options.ClientId),
            new("client_secret", options.ClientSecret),
            new("subject_token", subjectToken),
            new("subject_token_type", AccessTokenType),
            new("requested_token_type", AccessTokenType),
        };
        if (!string.IsNullOrWhiteSpace(scope))
        {
            form.Add(new KeyValuePair<string, string>("scope", scope));
        }
        request.Content = new FormUrlEncodedContent(form);

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Log the token-acquisition failure BEFORE EnsureSuccessStatusCode throws: the exception propagates
            // to the typed client and surfaces as a callee 503, so without this a Keycloak / realm-misconfig
            // failure looks like a callee (Basket/Catalog) outage in both the logs and the 503 message.
            await LogTokenAcquisitionFailureAsync(response, scope, cancellationToken).ConfigureAwait(false);
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<TokenResponsePayload>(
            stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (payload is null || string.IsNullOrEmpty(payload.AccessToken) || payload.ExpiresIn <= 0)
        {
            throw new InvalidOperationException(
                "Keycloak token-exchange endpoint returned an empty or malformed response.");
        }

        _logger.LogDebug(
            "Exchanged user token for scope='{Scope}' expires_in={ExpiresIn}s", scope, payload.ExpiresIn);

        return new CachedToken(payload.AccessToken, _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(payload.ExpiresIn));
    }

    private async Task LogTokenAcquisitionFailureAsync(
        HttpResponseMessage response,
        string scope,
        CancellationToken cancellationToken)
    {
        var (error, errorDescription) = await ReadOAuthErrorAsync(response, cancellationToken).ConfigureAwait(false);

        _logger.LogError(
            "Keycloak token acquisition failed: grant=token-exchange scope='{Scope}' "
            + "status={StatusCode} error={Error} error_description={ErrorDescription}. "
            + "This is a token-exchange/Keycloak failure, not a callee outage.",
            scope,
            (int)response.StatusCode,
            error,
            errorDescription);
    }

    // Extracts only the standard OAuth2 error fields from a non-success token response; never logs the raw
    // body (which could be an unexpected shape) and never the subject token/secret. Defensive: an empty or
    // non-OAuth body yields nulls so the caller still logs a status-only error rather than masking the failure.
    private static async Task<(string? Error, string? ErrorDescription)> ReadOAuthErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return (null, null);
            }

            var oauthError = JsonSerializer.Deserialize<OAuthErrorPayload>(body);
            return (oauthError?.Error, oauthError?.ErrorDescription);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException or InvalidOperationException)
        {
            return (null, null);
        }
    }

    private static string ResolveScope(HttpRequestMessage request) =>
        request.Options.TryGetValue(ScopeRequestOptionKey, out var scope) ? scope : string.Empty;

    private (string SubjectToken, string Sub) ResolveUser()
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "Token exchange requires an active HTTP request context; none is available. The buyer-scoped "
                + "outbound client must only be called while handling an authenticated user request.");

        var subjectToken = ExtractBearer(httpContext.Request.Headers.Authorization.ToString())
            ?? throw new InvalidOperationException(
                "Token exchange requires an inbound user bearer token, but the Authorization header is absent "
                + "or not a Bearer token.");

        var sub = httpContext.User.FindFirstValue("sub")
            ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException(
                "Token exchange requires a 'sub' claim on the authenticated user, but none is present.");

        return (subjectToken, sub);
    }

    private static string? ExtractBearer(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return null;
        }

        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorizationHeader[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    private sealed record TokenResponsePayload(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record OAuthErrorPayload(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
