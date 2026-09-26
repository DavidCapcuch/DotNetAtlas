using System.Net.Http.Headers;
using FastEndpoints.Testing;

namespace Ordering.IntegrationTests.Common.TestClientInfrastructure;

/// <summary>
/// One pre-built <see cref="HttpClient"/> per <see cref="ClientType"/>, each carrying a properly
/// signed Bearer token issued by <see cref="FakeTokenCreator"/>.
/// </summary>
public sealed class HttpClientRegistry<TEntryPoint>
    where TEntryPoint : class
{
    private readonly AppFixture<TEntryPoint> _appFixture;
    private readonly FakeTokenCreator _tokenCreator;
    private readonly Dictionary<ClientType, HttpClient> _clients = [];

    public HttpClientRegistry(AppFixture<TEntryPoint> appFixture, FakeTokenCreator tokenCreator)
    {
        _appFixture = appFixture;
        _tokenCreator = tokenCreator;
        foreach (var clientType in Enum.GetValues<ClientType>())
        {
            _clients[clientType] = CreateHttpClient(clientType);
        }
    }

    public HttpClient NonAuthClient => _clients[ClientType.NonAuth];
    public HttpClient BuyerClient => _clients[ClientType.Buyer];
    public HttpClient OtherBuyerClient => _clients[ClientType.OtherBuyer];
    public HttpClient AdminClient => _clients[ClientType.Admin];

    /// <summary>
    /// Points every pre-built client at the current test's trace, so the server spans a request
    /// produces join that test's trace instead of starting a detached one.
    /// </summary>
    public void SetTraceParent(string? traceParent)
    {
        foreach (var (_, client) in _clients)
        {
            client.DefaultRequestHeaders.Remove("traceparent");
            if (!string.IsNullOrWhiteSpace(traceParent))
            {
                client.DefaultRequestHeaders.Add("traceparent", traceParent);
            }
        }
    }

    private HttpClient CreateHttpClient(ClientType clientType)
    {
        return _appFixture.CreateClient(client =>
        {
            var token = _tokenCreator.CreateUserToken(clientType);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", string.IsNullOrEmpty(token) ? null : token);
        });
    }
}
