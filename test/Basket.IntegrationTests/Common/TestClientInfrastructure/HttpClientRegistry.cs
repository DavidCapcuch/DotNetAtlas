using System.Net.Http.Headers;
using FastEndpoints.Testing;

namespace Basket.FunctionalTests.Common.TestClientInfrastructure;

public sealed class HttpClientRegistry<TEntryPoint>
    where TEntryPoint : class
{
    private readonly AppFixture<TEntryPoint> _appFixture;
    private readonly FakeTokenCreator _tokenCreator;
    private readonly HttpClient _nonAuthClient;
    private string? _traceParent;

    public HttpClientRegistry(AppFixture<TEntryPoint> appFixture, FakeTokenCreator tokenCreator)
    {
        _appFixture = appFixture;
        _tokenCreator = tokenCreator;
        _nonAuthClient = CreateNonAuthClient();
    }

    public HttpClient NonAuthClient => _nonAuthClient;

    /// <summary>
    /// Builds a fresh HttpClient carrying a Bearer token for <paramref name="userId"/>. Each
    /// call returns a new client so per-test idempotency-key scenarios stay isolated.
    /// </summary>
    public HttpClient RegularUserAuthClient(Guid userId)
    {
        var token = _tokenCreator.CreateUserToken(ClientType.RegularUser, userId);
        return _appFixture.CreateClient(client =>
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            ApplyTraceParent(client);
        });
    }

    public void SetTraceParent(string? traceParent)
    {
        _traceParent = traceParent;
        ApplyTraceParent(_nonAuthClient);
    }

    private void ApplyTraceParent(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("traceparent");
        if (!string.IsNullOrWhiteSpace(_traceParent))
        {
            client.DefaultRequestHeaders.Add("traceparent", _traceParent);
        }
    }

    private HttpClient CreateNonAuthClient()
    {
        return _appFixture.CreateClient(client =>
        {
            client.DefaultRequestHeaders.Authorization = null;
            ApplyTraceParent(client);
        });
    }
}
