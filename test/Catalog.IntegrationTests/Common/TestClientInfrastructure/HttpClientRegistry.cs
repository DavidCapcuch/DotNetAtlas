using System.Net.Http.Headers;
using FastEndpoints.Testing;

namespace Catalog.IntegrationTests.Common.TestClientInfrastructure;

/// <summary>
/// Pre-builds one <see cref="HttpClient"/> per <see cref="ClientType"/>, each carrying a properly
/// signed JWT with the right Catalog scope claim. Tests pick the client matching the policy they
/// exercise.
/// </summary>
public sealed class HttpClientRegistry<TEntryPoint>
    where TEntryPoint : class
{
    private readonly AppFixture<TEntryPoint> _appFixture;
    private readonly FakeTokenCreator _tokenCreator;
    private readonly Dictionary<ClientType, HttpClient> _clients = [];
    private string? _traceParent;

    public HttpClientRegistry(AppFixture<TEntryPoint> appFixture, FakeTokenCreator tokenCreator)
    {
        _appFixture = appFixture;
        _tokenCreator = tokenCreator;
        foreach (var clientType in Enum.GetValues<ClientType>())
        {
            _clients[clientType] = Build(clientType);
        }
    }

    public HttpClient NonAuthClient => _clients[ClientType.NonAuth];

    public HttpClient ReadClient => _clients[ClientType.ReadOnly];

    public HttpClient WriteClient => _clients[ClientType.WriteAdmin];

    /// <summary>
    /// Token holds <c>catalog.write</c> but not the <c>admin</c> role — exercises the role half
    /// of the defense-in-depth write gate (must be rejected with 403).
    /// </summary>
    public HttpClient WriteScopeNoAdminClient => _clients[ClientType.WriteScopeNoAdmin];

    /// <summary>
    /// Builds a fresh <see cref="HttpClient"/> for the given <paramref name="clientType"/>,
    /// useful when a test needs per-call isolation (e.g. attaching a distinct
    /// <c>Idempotency-Key</c> header).
    /// </summary>
    public HttpClient CreateFresh(ClientType clientType)
    {
        return Build(clientType);
    }

    /// <summary>
    /// Points every client this registry hands out — pre-built and fresh — at the current test's
    /// trace, so the server spans a request produces join that test's trace instead of starting a
    /// detached one.
    /// </summary>
    public void SetTraceParent(string? traceParent)
    {
        _traceParent = traceParent;
        foreach (var client in _clients.Values)
        {
            client.DefaultRequestHeaders.Remove("traceparent");
            if (!string.IsNullOrWhiteSpace(traceParent))
            {
                client.DefaultRequestHeaders.Add("traceparent", traceParent);
            }
        }
    }

    private HttpClient Build(ClientType clientType)
    {
        return _appFixture.CreateClient(client =>
        {
            var token = _tokenCreator.CreateToken(clientType);
            client.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(token)
                ? null
                : new AuthenticationHeaderValue("Bearer", token);

            if (!string.IsNullOrWhiteSpace(_traceParent))
            {
                client.DefaultRequestHeaders.Add("traceparent", _traceParent);
            }
        });
    }
}
