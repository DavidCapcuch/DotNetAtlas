using System.Net.Http.Headers;
using FastEndpoints.Testing;

namespace Invoicing.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Pre-built typed <see cref="HttpClient"/>s, one per <see cref="ClientType"/>, each
/// carrying a properly signed Bearer token issued by <see cref="FakeTokenCreator"/>.
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

    public HttpClient this[ClientType clientType] => _clients[clientType];

    public HttpClient NonAuthClient => _clients[ClientType.NonAuth];
    public HttpClient BuyerClient => _clients[ClientType.Buyer];
    public HttpClient OtherBuyerClient => _clients[ClientType.OtherBuyer];
    public HttpClient AdminClient => _clients[ClientType.Admin];

    public HttpClient CreateHttpClient(ClientType clientType, string? traceParent = null)
    {
        return _appFixture.CreateClient(client =>
        {
            var token = _tokenCreator.CreateUserToken(clientType);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", string.IsNullOrEmpty(token) ? null : token);

            if (!string.IsNullOrWhiteSpace(traceParent))
            {
                client.DefaultRequestHeaders.Add("traceparent", traceParent);
            }
        });
    }

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
}
