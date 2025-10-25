using System.Net.Http.Headers;
using FastEndpoints.Testing;

namespace DotNetAtlas.FunctionalTests.Common.Clients;

public sealed class HttpClientRegistry<TEntryPoint>
    where TEntryPoint : class
{
    private readonly AppFixture<TEntryPoint> _appFixture;
    private readonly Dictionary<ClientType, HttpClient> _clients = [];

    public HttpClientRegistry(AppFixture<TEntryPoint> appFixture)
    {
        _appFixture = appFixture;
        foreach (var clientType in Enum.GetValues<ClientType>())
        {
            _clients[clientType] = CreateHttpClient(clientType);
        }
    }

    public HttpClient this[ClientType clientType] => _clients[clientType];

    public HttpClient NonAuthClient => _clients[ClientType.NonAuth];
    public HttpClient DevClient => _clients[ClientType.Dev];
    public HttpClient PlebClient => _clients[ClientType.Pleb];

    public HttpClient CreateHttpClient(
        ClientType clientType,
        string? traceParent = null)
    {
        return _appFixture.CreateClient(client =>
        {
            var token = FakeTokenCreator.CreateUserToken(clientType);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", string.IsNullOrEmpty(token) ? null : token);

            if (!string.IsNullOrWhiteSpace(traceParent))
            {
                client.DefaultRequestHeaders.Add("traceparent", traceParent);
            }
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
    /// This method removes any existing traceparent header and sets a new one for all clients in the registry,
    /// enabling correlation of HTTP requests.
    /// </remarks>
    public void SetTraceParent(string? traceParent)
    {
        foreach (var (_, client) in _clients)
        {
            client.DefaultRequestHeaders.Remove("traceparent");
            client.DefaultRequestHeaders.Add("traceparent", traceParent);
        }
    }
}
