using System.Net.Http.Headers;
using FastEndpoints.Testing;

namespace Inventory.FunctionalTests.Common.TestClientInfrastructure;

public sealed class HttpClientRegistry<TEntryPoint>
    where TEntryPoint : class
{
    private readonly AppFixture<TEntryPoint> _appFixture;
    private readonly FakeTokenCreator _tokenCreator;
    private readonly HttpClient _nonAuthClient;
    private readonly HttpClient _readOnlyClient;
    private readonly HttpClient _commandsClient;
    private readonly HttpClient _writeScopeNoAdminClient;

    public HttpClientRegistry(AppFixture<TEntryPoint> appFixture, FakeTokenCreator tokenCreator)
    {
        _appFixture = appFixture;
        _tokenCreator = tokenCreator;
        _nonAuthClient = CreateClientFor(ClientType.NonAuth);
        _readOnlyClient = CreateClientFor(ClientType.ReadOnly);
        _commandsClient = CreateClientFor(ClientType.Commands);
        _writeScopeNoAdminClient = CreateClientFor(ClientType.WriteScopeNoAdmin);
    }

    /// <summary>Bare client; no Authorization header. Drives the 401 branch.</summary>
    public HttpClient NonAuthClient => _nonAuthClient;

    /// <summary>JWT carries only <c>inventory.read</c>. Drives the 403 branch on admin POSTs.</summary>
    public HttpClient ReadOnlyClient => _readOnlyClient;

    /// <summary>JWT carries the <c>admin</c> role + <c>inventory.write</c> scope. Drives the success path on every endpoint.</summary>
    public HttpClient CommandsClient => _commandsClient;

    /// <summary>JWT carries the <c>inventory.write</c> scope but NOT the <c>admin</c> role. Drives the 403 branch on write endpoints (proves WritePolicy's role half).</summary>
    public HttpClient WriteScopeNoAdminClient => _writeScopeNoAdminClient;

    /// <summary>Per-test client carrying a specific <c>Idempotency-Key</c> header.</summary>
    public HttpClient CommandsClientWithIdempotencyKey(string idempotencyKey)
    {
        var token = _tokenCreator.CreateToken(ClientType.Commands);
        return _appFixture.CreateClient(client =>
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        });
    }

    /// <summary>
    /// Updates the traceparent header for all registered HTTP clients to establish a distributed tracing context.
    /// </summary>
    /// <param name="traceParent">
    /// The W3C Trace Context traceparent header value (format: version-trace-id-parent-id-trace-flags).
    /// If null, removes the traceparent header from all clients.
    /// </param>
    /// <remarks>
    /// This method removes any existing traceparent header and sets a new one for all registry-managed clients,
    /// enabling correlation of HTTP requests with the test's Jaeger trace.
    /// </remarks>
    public void SetTraceParent(string? traceParent)
    {
        foreach (var client in new[] { _nonAuthClient, _readOnlyClient, _commandsClient, _writeScopeNoAdminClient })
        {
            client.DefaultRequestHeaders.Remove("traceparent");
            client.DefaultRequestHeaders.Add("traceparent", traceParent);
        }
    }

    private HttpClient CreateClientFor(ClientType clientType)
    {
        if (clientType == ClientType.NonAuth)
        {
            return _appFixture.CreateClient(client =>
                client.DefaultRequestHeaders.Authorization = null);
        }

        var token = _tokenCreator.CreateToken(clientType);
        return _appFixture.CreateClient(client =>
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token));
    }
}
