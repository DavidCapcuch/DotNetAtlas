using System.Security.Claims;
using EShop.BFF.Api.Responses;
using EShop.BFF.Infrastructure.Caching;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Test.Framework.Auth;
using Platform.Test.Framework.Redis;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.IntegrationTests.Common;

internal sealed class BasketPageTestCollection : TestCollection<BasketPageTestFixture>;

/// <summary>
/// Boots the BFF over a real <c>redis-cache</c> Testcontainer with Basket, Catalog, Inventory, and the
/// Keycloak token endpoint faked by a single WireMock server. Exercises the real typed clients — including
/// the <b>RFC 8693 token-exchange</b> Basket client and the <c>client_credentials</c> Catalog / Inventory
/// clients — and the real per-user FusionCache; only the upstreams are stubbed. Inbound user JWTs are
/// validated for real against a <see cref="FakeTokenSigner"/> (audience <c>bff</c>), so the required-auth
/// gate runs end-to-end. Each test uses a fresh UserId, so per-scenario stubs never collide.
/// </summary>
[DisableWafCache]
public sealed class BasketPageTestFixture : AppFixture<Program>
{
    private readonly RedisTestContainer _redisContainer = new();
    private readonly FakeTokenSigner _signer = new(audience: "bff");

    private WireMockServer _upstreams = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await _redisContainer.StartAsync();

        _upstreams = WireMockServer.Start();

        // Both outbound shapes (client_credentials for Catalog/Inventory, RFC 8693 token exchange for
        // Basket) POST to the same Keycloak token endpoint — one stub returns a fake token for either grant.
        StubTokenEndpoint();
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            var upstreamUrl = _upstreams.Url!;
            webBuilder
                .UseSetting("ConnectionStrings:Redis:Cache", _redisContainer.ConfigurationOptions.ToString())
                .UseSetting("Bff:Catalog:BaseUrl", upstreamUrl)
                .UseSetting("Bff:Inventory:BaseUrl", upstreamUrl)
                .UseSetting("Bff:Basket:BaseUrl", upstreamUrl)
                .UseSetting("ServiceAuth:Authority", $"{upstreamUrl}/realms/dotnetatlas")
                .UseSetting("ServiceAuth:ClientId", "bff")
                .UseSetting("ServiceAuth:ClientSecret", "test-secret")
                .UseSetting("ServiceAuth:ServiceName", "bff")
                .UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
        });

        return base.ConfigureAppHost(a);
    }

    protected override void ConfigureApp(IWebHostBuilder a) =>
        a
            .UseEnvironment("Testing")
            .UseTestSerilog()
            .UseWarmFlag(enabled: false)
            // Trust the fake signer's RSA key so the inbound user-JWT validation runs for real (the BFF's
            // ValidAudience = bff, asserted to match the signer's audience by ConfigureJwtBearerForTests).
            .ConfigureTestServices(services => services.ConfigureJwtBearerForTests(_signer));

    /// <summary>Mints a user JWT (aud <c>bff</c>) carrying the buyer <c>sub</c>, as the inbound credential.</summary>
    public string CreateUserToken(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
        };
        return FakeTokenBuilder.SignToken(_signer, claims);
    }

    /// <summary>Stubs Basket's <c>GET /api/v1/basket</c> with a 200 basket body.</summary>
    public void StubBasket(object body) => StubGet("/api/v1/basket", 200, body);

    /// <summary>Stubs Basket's read with a bare status code (e.g. 404 / 500).</summary>
    public void StubBasketStatus(int statusCode) => StubGet("/api/v1/basket", statusCode, body: null);

    /// <summary>Stubs Catalog's <c>GET /api/v1/catalog/products/by-ids</c> with a 200 batch body.</summary>
    public void StubCatalogByIds(object body) => StubGet("/api/v1/catalog/products/by-ids", 200, body);

    /// <summary>Stubs Catalog's by-ids read with a bare status code (e.g. 500).</summary>
    public void StubCatalogByIdsStatus(int statusCode) =>
        StubGet("/api/v1/catalog/products/by-ids", statusCode, body: null);

    /// <summary>Stubs Inventory's <c>POST /api/v1/inventory/stock-items/bulk</c> with a 200 bulk body.</summary>
    public void StubInventoryBulk(object body) => StubPost("/api/v1/inventory/stock-items/bulk", 200, body);

    /// <summary>Stubs Inventory's bulk read with a bare status code (e.g. 500).</summary>
    public void StubInventoryBulkStatus(int statusCode) =>
        StubPost("/api/v1/inventory/stock-items/bulk", statusCode, body: null);

    /// <summary>Wipes all upstream stubs (re-adding only the token endpoint) so a test can flip health mid-run.</summary>
    public void ResetUpstreams()
    {
        _upstreams.Reset();
        StubTokenEndpoint();
    }

    public async Task<bool> IsBasketCachedAsync(Guid userId)
    {
        var cache = Services.GetRequiredService<IFusionCache>();
        var maybe = await cache.TryGetAsync<BasketPageResponse>(BffCacheConstants.BasketPageKey(userId));
        return maybe.HasValue;
    }

    public async Task ResetFixtureStateAsync() => await _redisContainer.CleanDataAsync();

    protected override async ValueTask TearDownAsync()
    {
        _signer.Dispose();
        _upstreams.Stop();
        _upstreams.Dispose();
        await _redisContainer.DisposeAsync();
    }

    private void StubTokenEndpoint() =>
        _upstreams
            .Given(Request.Create()
                .WithPath("/realms/dotnetatlas/protocol/openid-connect/token")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new { access_token = "fake-exchanged-token", expires_in = 300, token_type = "Bearer" }));

    private void StubGet(string path, int statusCode, object? body) =>
        Stub(Request.Create().WithPath(path).UsingGet(), statusCode, body);

    private void StubPost(string path, int statusCode, object? body) =>
        Stub(Request.Create().WithPath(path).UsingPost(), statusCode, body);

    private void Stub(IRequestBuilder request, int statusCode, object? body)
    {
        var response = Response.Create().WithStatusCode(statusCode);
        if (body is not null)
        {
            response = response.WithHeader("Content-Type", "application/json").WithBodyAsJson(body);
        }

        _upstreams.Given(request).RespondWith(response);
    }
}
