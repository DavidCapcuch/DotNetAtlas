using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Auth;
using Platform.SharedKernel.Time;

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
        var clock = new FakeClock(Fixed);
        var (handler, target, tokenEndpoint) = Build(clock: clock, expiresInSeconds: 60);
        target.Respond(HttpStatusCode.OK);
        target.Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        _ = await client.GetAsync(new Uri("http://downstream/ping"), TestContext.Current.CancellationToken);
        // Advance into the 30s buffer (token expires at +60s, buffer is 30s → refresh at +30s+).
        clock.Advance(TimeSpan.FromSeconds(35));

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
        FakeClock? clock = null,
        int expiresInSeconds = 3600,
        TimeSpan? tokenFetchDelay = null)
    {
        clock ??= new FakeClock(Fixed);

        var tokenEndpoint = new TokenEndpointHandler(expiresInSeconds, tokenFetchDelay);
        var factory = new StubHttpClientFactory(tokenEndpoint);

        var options = new ServiceAuthOptions
        {
            Authority = "http://keycloak/realms/test",
            ClientId = "svc",
            ClientSecret = "secret",
            ServiceName = "svc",
        };
        var monitor = new TestOptionsMonitor<ServiceAuthOptions>(options);

        var handler = new ClientCredentialsTokenHandler(
            monitor, factory, clock, NullLogger<ClientCredentialsTokenHandler>.Instance);

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

    private sealed class TokenEndpointHandler(int expiresInSeconds, TimeSpan? delay) : HttpMessageHandler
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

            var payload = new { access_token = $"token-{call}", expires_in = expiresInSeconds };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        }
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
