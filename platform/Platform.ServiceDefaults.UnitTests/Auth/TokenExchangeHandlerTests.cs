using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Platform.ServiceDefaults.Auth;

namespace Platform.ServiceDefaults.UnitTests.Auth;

public class TokenExchangeHandlerTests
{
    private static readonly DateTimeOffset Fixed = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid BuyerA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BuyerB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SendAsync_WhenCacheMiss_ExchangesInboundUserTokenAndAttachesExchangedBearer()
    {
        // Arrange
        var (handler, target, tokenEndpoint, _) = Build(userToken: "user-jwt-A", sub: BuyerA);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        // Act
        using var response = await SendAsync(client, "basket.read");

        // Assert — the exchanged token (not the user JWT) is attached, and the exchange request is RFC 8693.
        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        target.LastAuthHeader.Should().Be(new AuthenticationHeaderValue("Bearer", "exchanged-1"));
        tokenEndpoint.CallCount.Should().Be(1);
        tokenEndpoint.LastForm.Should().Contain("grant_type", "urn:ietf:params:oauth:grant-type:token-exchange");
        tokenEndpoint.LastForm.Should().Contain("subject_token", "user-jwt-A");
        tokenEndpoint.LastForm.Should().Contain("subject_token_type", "urn:ietf:params:oauth:token-type:access_token");
        tokenEndpoint.LastForm.Should().Contain("scope", "basket.read");
        tokenEndpoint.LastForm.Should().Contain("client_id", "bff");
    }

    [Fact]
    public async Task SendAsync_WhenCacheHitWithinBuffer_ReusesExchangedToken()
    {
        // Arrange
        var (handler, target, tokenEndpoint, _) = Build(userToken: "user-jwt-A", sub: BuyerA);
        target.Respond(HttpStatusCode.OK);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        // Act — same (sub, scope) twice; only one exchange expected.
        (await SendAsync(client, "basket.read")).Dispose();
        (await SendAsync(client, "basket.read")).Dispose();

        // Assert
        tokenEndpoint.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_PerUserKey_NeverServesOneUsersTokenToAnother()
    {
        // Arrange — this is the load-bearing security property: the (sub, scope) cache must never let
        // buyer B's outbound call carry buyer A's exchanged token (the callee resolves the owner from sub).
        var (handler, target, tokenEndpoint, accessor) = Build(userToken: "user-jwt-A", sub: BuyerA);
        for (var i = 0; i < 4; i++)
        {
            target.Respond(HttpStatusCode.OK);
        }

        using var client = new HttpClient(handler);

        // Act — A, then B, then A again, then B again (same scope throughout).
        (await SendAsync(client, "basket.read")).Dispose();
        var tokenForA = target.LastAuthHeader;

        accessor.HttpContext = BuildHttpContext("user-jwt-B", BuyerB);
        (await SendAsync(client, "basket.read")).Dispose();
        var tokenForB = target.LastAuthHeader;

        accessor.HttpContext = BuildHttpContext("user-jwt-A", BuyerA);
        (await SendAsync(client, "basket.read")).Dispose();
        var tokenForASecond = target.LastAuthHeader;

        accessor.HttpContext = BuildHttpContext("user-jwt-B", BuyerB);
        (await SendAsync(client, "basket.read")).Dispose();
        var tokenForBSecond = target.LastAuthHeader;

        // Assert — distinct tokens per buyer, each cached per-user (only two exchanges total).
        using var _ = new AssertionScope();
        tokenEndpoint.CallCount.Should().Be(2);
        tokenForA.Should().Be(new AuthenticationHeaderValue("Bearer", "exchanged-1"));
        tokenForB.Should().Be(new AuthenticationHeaderValue("Bearer", "exchanged-2"));
        tokenForB.Should().NotBe(tokenForA);
        tokenForASecond.Should().Be(tokenForA, "buyer A's cached token is reused, not re-fetched");
        tokenForBSecond.Should().Be(tokenForB, "buyer B's cached token is reused, not re-fetched");
    }

    [Fact]
    public async Task SendAsync_PerScopeKey_IsolatesEntriesForTheSameUser()
    {
        // Arrange
        var (handler, target, tokenEndpoint, _) = Build(userToken: "user-jwt-A", sub: BuyerA);
        target.Respond(HttpStatusCode.OK);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        // Act — same user, different scope (basket.read vs ordering.read) → distinct callee audiences.
        (await SendAsync(client, "basket.read")).Dispose();
        (await SendAsync(client, "ordering.read")).Dispose();

        // Assert
        tokenEndpoint.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_WhenCachedTokenInsideThirtySecondBuffer_RefreshesToken()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Fixed);
        var (handler, target, tokenEndpoint, _) = Build(
            userToken: "user-jwt-A", sub: BuyerA, timeProvider: timeProvider, expiresInSeconds: 60);
        target.Respond(HttpStatusCode.OK);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        (await SendAsync(client, "basket.read")).Dispose();
        // Advance into the 30s buffer (token expires at +60s, buffer is 30s → refresh at +30s+).
        timeProvider.Advance(TimeSpan.FromSeconds(35));

        // Act
        (await SendAsync(client, "basket.read")).Dispose();

        // Assert
        tokenEndpoint.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_When401FromCallee_InvalidatesCacheAndRetriesOnce()
    {
        // Arrange
        var (handler, target, tokenEndpoint, _) = Build(userToken: "user-jwt-A", sub: BuyerA);
        target.Respond(HttpStatusCode.Unauthorized);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        // Act
        using var response = await SendAsync(client, "basket.read");

        // Assert — re-exchanged and retried once.
        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        target.CallCount.Should().Be(2);
        tokenEndpoint.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_When401Again_DoesNotLoop()
    {
        // Arrange
        var (handler, target, tokenEndpoint, _) = Build(userToken: "user-jwt-A", sub: BuyerA);
        target.Respond(HttpStatusCode.Unauthorized);
        target.Respond(HttpStatusCode.Unauthorized);
        using var client = new HttpClient(handler);

        // Act
        using var response = await SendAsync(client, "basket.read");

        // Assert — exactly one retry, then surfaces the 401.
        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        target.CallCount.Should().Be(2);
        tokenEndpoint.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_ConcurrentFirstCalls_IssuesOneExchange()
    {
        // Arrange
        var (handler, target, tokenEndpoint, _) = Build(
            userToken: "user-jwt-A", sub: BuyerA, tokenFetchDelay: TimeSpan.FromMilliseconds(50));
        for (var i = 0; i < 20; i++)
        {
            target.Respond(HttpStatusCode.OK);
        }

        using var client = new HttpClient(handler);

        // Act — 20 parallel sends before the first exchange completes.
        var tasks = Enumerable.Range(0, 20).Select(_ => SendAsync(client, "basket.read")).ToArray();
        var responses = await Task.WhenAll(tasks);
        foreach (var r in responses)
        {
            r.Dispose();
        }

        // Assert — single-flight collapses them into one exchange.
        tokenEndpoint.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenTokenEndpointReturnsNonSuccess_LogsTokenAcquisitionFailureNamingKeycloak()
    {
        // Arrange — Keycloak rejects the exchange (e.g. the scope's audience-mapper is misconfigured in the realm).
        var logger = new CapturingLogger<TokenExchangeHandler>();
        var (handler, _, _, _) = Build(
            userToken: "user-jwt-A",
            sub: BuyerA,
            logger: logger,
            tokenEndpointStatus: HttpStatusCode.BadRequest,
            tokenEndpointErrorBody: """{"error":"invalid_scope","error_description":"Invalid scopes: basket.read"}""");
        using var client = new HttpClient(handler);

        // Act — the exchange fails, so the request still surfaces the failure (EnsureSuccessStatusCode throws) ...
        var act = async () => (await SendAsync(client, "basket.read")).Dispose();

        // Assert — ... but FIRST a token-acquisition-specific error is logged naming the grant, scope, Keycloak
        // status and OAuth error, so a realm-misconfig is distinguishable from a callee outage (no token logged).
        await act.Should().ThrowAsync<HttpRequestException>();

        using var _ = new AssertionScope();
        var error = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which;
        error.Message.Should().Contain("token-exchange");
        error.Message.Should().Contain("basket.read");
        error.Message.Should().Contain("400");
        error.Message.Should().Contain("invalid_scope");
        error.Message.Should().NotContain("user-jwt-A");
        error.Message.Should().NotContain("super-secret-value");
    }

    [Fact]
    public async Task SendAsync_WhenTokenEndpointReturnsNonSuccessWithUnparseableBody_StillLogsErrorWithStatus()
    {
        // Arrange — a non-OAuth body (e.g. an HTML 502 from a proxy in front of Keycloak).
        var logger = new CapturingLogger<TokenExchangeHandler>();
        var (handler, _, _, _) = Build(
            userToken: "user-jwt-A",
            sub: BuyerA,
            logger: logger,
            tokenEndpointStatus: HttpStatusCode.BadGateway,
            tokenEndpointErrorBody: "<html>oops</html>");
        using var client = new HttpClient(handler);

        // Act
        var act = async () => (await SendAsync(client, "basket.read")).Dispose();

        // Assert — body parsing is defensive: unparseable body falls back to a status-only error, never throwing
        // from the logging path (the underlying status failure still surfaces).
        await act.Should().ThrowAsync<HttpRequestException>();

        using var _ = new AssertionScope();
        var error = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which;
        error.Message.Should().Contain("token-exchange");
        error.Message.Should().Contain("502");
    }

    [Fact]
    public async Task SendAsync_WhenNoHttpContext_ThrowsFailClosed()
    {
        // Arrange — a buyer-scoped client must only run inside an authenticated request.
        var (handler, target, _, accessor) = Build(userToken: "user-jwt-A", sub: BuyerA);
        accessor.HttpContext = null;
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        // Act
        var act = async () => (await SendAsync(client, "basket.read")).Dispose();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendAsync_WhenNoInboundBearer_ThrowsFailClosed()
    {
        // Arrange
        var (handler, target, _, accessor) = Build(userToken: "user-jwt-A", sub: BuyerA);
        accessor.HttpContext = BuildHttpContext(userToken: null, sub: BuyerA);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        // Act
        var act = async () => (await SendAsync(client, "basket.read")).Dispose();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendAsync_WhenNoSubClaim_ThrowsFailClosed()
    {
        // Arrange
        var (handler, target, _, accessor) = Build(userToken: "user-jwt-A", sub: BuyerA);
        accessor.HttpContext = BuildHttpContext(userToken: "user-jwt-A", sub: null);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        // Act
        var act = async () => (await SendAsync(client, "basket.read")).Dispose();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string scope)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://basket/api/v1/basket"));
        request.Options.Set(TokenExchangeHandler.ScopeRequestOptionKey, scope);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static (TokenExchangeHandler Handler, StubMessageHandler Target, TokenEndpointHandler TokenEndpoint, HttpContextAccessor Accessor) Build(
        string userToken,
        Guid sub,
        FakeTimeProvider? timeProvider = null,
        int expiresInSeconds = 3600,
        TimeSpan? tokenFetchDelay = null,
        ILogger<TokenExchangeHandler>? logger = null,
        HttpStatusCode tokenEndpointStatus = HttpStatusCode.OK,
        string? tokenEndpointErrorBody = null)
    {
        timeProvider ??= new FakeTimeProvider(Fixed);

        var tokenEndpoint = new TokenEndpointHandler(
            expiresInSeconds, tokenFetchDelay, tokenEndpointStatus, tokenEndpointErrorBody);
        var factory = new StubHttpClientFactory(tokenEndpoint);

        var options = new ServiceAuthOptions
        {
            Authority = "http://keycloak/realms/test",
            ClientId = "bff",
            ClientSecret = "super-secret-value",
            ServiceName = "bff",
        };
        var monitor = new TestOptionsMonitor<ServiceAuthOptions>(options);
        var accessor = new HttpContextAccessor { HttpContext = BuildHttpContext(userToken, sub) };

        var handler = new TokenExchangeHandler(
            monitor, factory, accessor, timeProvider, logger ?? NullLogger<TokenExchangeHandler>.Instance);

        var target = new StubMessageHandler();
        handler.InnerHandler = target;
        return (handler, target, tokenEndpoint, accessor);
    }

    private static DefaultHttpContext BuildHttpContext(string? userToken, Guid? sub)
    {
        var context = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(userToken))
        {
            context.Request.Headers.Authorization = $"Bearer {userToken}";
        }

        var identity = new ClaimsIdentity(authenticationType: sub is null ? null : "test");
        if (sub is { } s)
        {
            identity.AddClaim(new Claim("sub", s.ToString()));
        }

        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    private sealed class StubMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _responses = new();

        public int CallCount { get; private set; }
        public AuthenticationHeaderValue? LastAuthHeader { get; private set; }

        public void Respond(HttpStatusCode status) => _responses.Enqueue(status);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            LastAuthHeader = request.Headers.Authorization;
            var status = _responses.Count > 0 ? _responses.Dequeue() : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private sealed class TokenEndpointHandler(
        int expiresInSeconds,
        TimeSpan? delay,
        HttpStatusCode status = HttpStatusCode.OK,
        string? errorBody = null) : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => _callCount;
        public IReadOnlyDictionary<string, string>? LastForm { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _callCount);
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            LastForm = ParseForm(body);

            if (delay is { } d)
            {
                await Task.Delay(d, ct).ConfigureAwait(false);
            }

            if (status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(status) { Content = new StringContent(errorBody ?? string.Empty) };
            }

            var payload = new { access_token = $"exchanged-{call}", expires_in = expiresInSeconds };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        }

        private static Dictionary<string, string> ParseForm(string body) =>
            body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(
                    parts => Uri.UnescapeDataString(parts[0]),
                    parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
    }

    private sealed class StubHttpClientFactory(TokenEndpointHandler tokenEndpoint) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(tokenEndpoint, disposeHandler: false);
    }

    private sealed class TestOptionsMonitor<T>(T current) : IOptionsMonitor<T>
    {
        public T CurrentValue => current;
        public T Get(string? name) => current;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
