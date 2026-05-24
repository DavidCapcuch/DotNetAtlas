using System.Net.Http.Headers;
using FastEndpoints.Testing;

namespace Catalog.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Pre-builds three <see cref="HttpClient"/> instances — one per <see cref="ClientType"/> —
/// each carrying a properly signed JWT with the right Catalog scope claim.
/// Tests pick the client matching the policy they exercise.
/// </summary>
public sealed class HttpClientRegistry<TEntryPoint>
    where TEntryPoint : class
{
    private readonly AppFixture<TEntryPoint> _appFixture;
    private readonly FakeTokenCreator _tokenCreator;
    private readonly HttpClient _nonAuthClient;
    private readonly HttpClient _readClient;
    private readonly HttpClient _writeClient;

    public HttpClientRegistry(AppFixture<TEntryPoint> appFixture, FakeTokenCreator tokenCreator)
    {
        _appFixture = appFixture;
        _tokenCreator = tokenCreator;
        _nonAuthClient = Build(ClientType.NonAuth);
        _readClient = Build(ClientType.ReadOnly);
        _writeClient = Build(ClientType.WriteAdmin);
    }

    public HttpClient NonAuthClient => _nonAuthClient;

    public HttpClient ReadClient => _readClient;

    public HttpClient WriteClient => _writeClient;

    /// <summary>
    /// Builds a fresh <see cref="HttpClient"/> for the given <paramref name="clientType"/>,
    /// useful when a test needs per-call isolation (e.g. attaching distinct
    /// <c>Idempotency-Key</c> / <c>X-Correlation-Id</c> headers).
    /// </summary>
    public HttpClient CreateFresh(ClientType clientType)
    {
        return Build(clientType);
    }

    private HttpClient Build(ClientType clientType)
    {
        return _appFixture.CreateClient(client =>
        {
            var token = _tokenCreator.CreateToken(clientType);
            client.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(token)
                ? null
                : new AuthenticationHeaderValue("Bearer", token);
        });
    }
}
