using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Platform.ServiceDefaults.Auth;

namespace Platform.ServiceDefaults.UnitTests.Auth;

public class ClientCredentialsTokenHandlerTests
{
    private static readonly DateTimeOffset Fixed = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SendAsync_WhenCacheMiss_FetchesTokenAndAttachesBearer()
    {
        // Arrange
        var (handler, target, tokenEndpoint) = Build();
        target.Respond(HttpStatusCode.OK);

        using var client = new HttpClient(handler);

        // Act
        using var response = await client.GetAsync(
            new Uri("http://downstream/ping"), TestContext.Current.CancellationToken);

        // Assert
        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        target.LastAuthHeader.Should().Be(new AuthenticationHeaderValue("Bearer", "token-1"));
        tokenEndpoint.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenCacheHitWithinBuffer_ReusesToken()
    {
        // Arrange
        var (handler, target, tokenEndpoint) = Build();
        target.Respond(HttpStatusCode.OK);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        // Act — two calls back-to-back, only one token fetch expected
        _ = await client.GetAsync(new Uri("http://downstream/ping"), TestContext.Current.CancellationToken);
        _ = await client.GetAsync(new Uri("http://downstream/ping"), TestContext.Current.CancellationToken);

        // Assert
        tokenEndpoint.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenCacheHitInsideThirtySecondBuffer_RefreshesToken()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(Fixed);
        var (handler, target, tokenEndpoint) = Build(timeProvider: timeProvider, expiresInSeconds: 60);
        target.Respond(HttpStatusCode.OK);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        _ = await client.GetAsync(new Uri("http://downstream/ping"), TestContext.Current.CancellationToken);
        // Advance into the 30s buffer (token expires at +60s, buffer is 30s → refresh at +30s+).
        timeProvider.Advance(TimeSpan.FromSeconds(35));

        // Act
        _ = await client.GetAsync(new Uri("http://downstream/ping"), TestContext.Current.CancellationToken);

        // Assert
        tokenEndpoint.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_When401FromTarget_InvalidatesCacheAndRetriesOnce()
    {
        // Arrange
        var (handler, target, tokenEndpoint) = Build();
        target.Respond(HttpStatusCode.Unauthorized);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        // Act
        using var response = await client.GetAsync(
            new Uri("http://downstream/ping"), TestContext.Current.CancellationToken);

        // Assert
        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        target.CallCount.Should().Be(2);
        tokenEndpoint.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_When401Again_DoesNotLoop()
    {
        // Arrange
        var (handler, target, tokenEndpoint) = Build();
        target.Respond(HttpStatusCode.Unauthorized);
        target.Respond(HttpStatusCode.Unauthorized);
        using var client = new HttpClient(handler);

        // Act
        using var response = await client.GetAsync(
            new Uri("http://downstream/ping"), TestContext.Current.CancellationToken);

        // Assert
        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        target.CallCount.Should().Be(2);
        tokenEndpoint.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_WhenTokenEndpointReturnsNonSuccess_LogsTokenAcquisitionFailureNamingKeycloak()
    {
        // Arrange — Keycloak rejects the token request (e.g. realm misconfig / bad client secret).
        var logger = new CapturingLogger<ClientCredentialsTokenHandler>();
        var (handler, _, _) = Build(
            logger: logger,
            tokenEndpointStatus: HttpStatusCode.BadRequest,
            tokenEndpointErrorBody: """{"error":"invalid_scope","error_description":"Invalid scopes: bogus"}""");
        using var client = new HttpClient(handler);

        // Act — acquisition fails, so the request still surfaces the failure (EnsureSuccessStatusCode throws) ...
        var act = async () =>
            (await client.GetAsync(new Uri("http://downstream/ping"), TestContext.Current.CancellationToken)).Dispose();

        // Assert — ... but FIRST a token-acquisition-specific error is logged naming the grant, Keycloak status
        // and OAuth error, so a token/Keycloak failure is distinguishable from a callee outage (no secret logged).
        await act.Should().ThrowAsync<HttpRequestException>();

        using var _ = new AssertionScope();
        var error = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which;
        error.Message.Should().Contain("client_credentials");
        error.Message.Should().Contain("400");
        error.Message.Should().Contain("invalid_scope");
        error.Message.Should().NotContain("super-secret-value");
    }

    [Fact]
    public async Task SendAsync_WhenTokenEndpointReturnsNonSuccessWithUnparseableBody_StillLogsErrorWithStatus()
    {
        // Arrange — a non-OAuth body (e.g. an HTML 502 from a proxy in front of Keycloak).
        var logger = new CapturingLogger<ClientCredentialsTokenHandler>();
        var (handler, _, _) = Build(
            logger: logger,
            tokenEndpointStatus: HttpStatusCode.BadGateway,
            tokenEndpointErrorBody: "<html>oops</html>");
        using var client = new HttpClient(handler);

        // Act
        var act = async () =>
            (await client.GetAsync(new Uri("http://downstream/ping"), TestContext.Current.CancellationToken)).Dispose();

        // Assert — body parsing is defensive: unparseable body falls back to a status-only error, never throwing
        // from the logging path (the underlying status failure still surfaces).
        await act.Should().ThrowAsync<HttpRequestException>();

        using var _ = new AssertionScope();
        var error = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which;
        error.Message.Should().Contain("client_credentials");
        error.Message.Should().Contain("502");
    }

    [Fact]
    public async Task SendAsync_PerScopeKey_IsolatesEntries()
    {
        // Arrange
        var (handler, target, tokenEndpoint) = Build();
        target.Respond(HttpStatusCode.OK);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        // Act — same host, different scope pinned per request.
        using var r1 = new HttpRequestMessage(HttpMethod.Get, new Uri("http://downstream/a"));
        r1.Options.Set(ClientCredentialsTokenHandler.ScopeRequestOptionKey, "scope.read");
        (await client.SendAsync(r1, TestContext.Current.CancellationToken)).Dispose();

        using var r2 = new HttpRequestMessage(HttpMethod.Get, new Uri("http://downstream/a"));
        r2.Options.Set(ClientCredentialsTokenHandler.ScopeRequestOptionKey, "scope.write");
        (await client.SendAsync(r2, TestContext.Current.CancellationToken)).Dispose();

        // Assert — distinct scopes → distinct cache entries → two token fetches.
        tokenEndpoint.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_ConcurrentFirstCalls_IssuesOneTokenFetch()
    {
        // Arrange
        var (handler, target, tokenEndpoint) = Build(tokenFetchDelay: TimeSpan.FromMilliseconds(50));
        for (int i = 0; i < 20; i++)
        {
            target.Respond(HttpStatusCode.OK);
        }

        using var client = new HttpClient(handler);

        // Act — 20 parallel sends before the first token fetch completes.
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => client.GetAsync(new Uri("http://downstream/ping"), TestContext.Current.CancellationToken))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        foreach (var r in responses)
        {
            r.Dispose();
        }

        // Assert
        tokenEndpoint.CallCount.Should().Be(1);
    }

    private static (ClientCredentialsTokenHandler Handler, StubMessageHandler Target, TokenEndpointHandler TokenEndpoint) Build(
        FakeTimeProvider? timeProvider = null,
        int expiresInSeconds = 3600,
        TimeSpan? tokenFetchDelay = null,
        ILogger<ClientCredentialsTokenHandler>? logger = null,
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
            ClientId = "svc",
            ClientSecret = "super-secret-value",
            ServiceName = "svc",
        };
        var monitor = new TestOptionsMonitor<ServiceAuthOptions>(options);

        var handler = new ClientCredentialsTokenHandler(
            monitor, factory, timeProvider, logger ?? NullLogger<ClientCredentialsTokenHandler>.Instance);

        var target = new StubMessageHandler();
        handler.InnerHandler = target;
        return (handler, target, tokenEndpoint);
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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _callCount);
            if (delay is { } d)
            {
                await Task.Delay(d, ct).ConfigureAwait(false);
            }

            if (status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(status) { Content = new StringContent(errorBody ?? string.Empty) };
            }

            var payload = new { access_token = $"token-{call}", expires_in = expiresInSeconds };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        }
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
}
