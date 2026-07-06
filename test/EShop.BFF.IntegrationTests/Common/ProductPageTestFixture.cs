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

internal sealed class ProductPageTestCollection : TestCollection<ProductPageTestFixture>;

/// <summary>
/// Boots the BFF over a real <c>redis-cache</c> Testcontainer with Catalog, Inventory, Basket, and the
/// Keycloak token endpoint faked by a single WireMock server. Exercises the real typed clients
/// (service-auth + resilience, plus the RFC 8693 token-exchange Basket client for the authenticated
/// <c>AlreadyInBasket</c> overlay) and the real FusionCache — only the upstreams are stubbed. Inbound user
/// JWTs validate for real against a <see cref="FakeTokenSigner"/> (audience <c>bff</c>). Each test uses a
/// fresh ProductId, so per-scenario stubs never collide and no per-test WireMock reset is needed.
/// </summary>
[DisableWafCache]
public sealed class ProductPageTestFixture : AppFixture<Program>
{
    private readonly RedisTestContainer _redisContainer = new();
    private readonly FakeTokenSigner _signer = new(audience: "bff");

    private WireMockServer _upstreams = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await _redisContainer.StartAsync();

        _upstreams = WireMockServer.Start();

        // ClientCredentialsTokenHandler fetches a service token before each upstream call
        // (ADR-0010); fake the Keycloak token endpoint so the real auth pipeline runs.
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
                // No OTLP collector in tests — skip the exporter wiring entirely.
                .UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
        });

        return base.ConfigureAppHost(a);
    }

    protected override void ConfigureApp(IWebHostBuilder a)
    {
        // Warm off — the product-page suite doesn't exercise the home-page warmer. Trust the fake signer so
        // the inbound user-JWT validation runs for real on the authenticated AlreadyInBasket-overlay path.
        a
            .UseEnvironment("Testing")
            .UseTestSerilog()
            .UseWarmFlag(enabled: false)
            .ConfigureTestServices(services => services.ConfigureJwtBearerForTests(_signer));
    }

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

    /// <summary>Stubs Basket's <c>GET /api/v1/basket</c> with a 200 basket body (the overlay source).</summary>
    public void StubBasket(object body) => StubGet("/api/v1/basket", 200, body);

    /// <summary>Stubs Basket's read with a bare status code (e.g. 404 / 500).</summary>
    public void StubBasketStatus(int statusCode) => StubGet("/api/v1/basket", statusCode, body: null);

    /// <summary>Stubs Catalog's <c>GET /api/v1/catalog/products/{id}</c> with a 200 product body.</summary>
    public void StubCatalogProduct(Guid productId, object body) =>
        StubGet($"/api/v1/catalog/products/{productId}", 200, body);

    /// <summary>Stubs Catalog's product read with a bare status code (e.g. 404 / 500).</summary>
    public void StubCatalogStatus(Guid productId, int statusCode) =>
        StubGet($"/api/v1/catalog/products/{productId}", statusCode, body: null);

    /// <summary>Stubs Inventory's <c>GET /api/v1/inventory/stock-items/{id}</c> with a 200 stock body.</summary>
    public void StubInventoryStock(Guid productId, object body) =>
        StubGet($"/api/v1/inventory/stock-items/{productId}", 200, body);

    /// <summary>Stubs Inventory's stock read with a bare status code (e.g. 500).</summary>
    public void StubInventoryStatus(Guid productId, int statusCode) =>
        StubGet($"/api/v1/inventory/stock-items/{productId}", statusCode, body: null);

    /// <summary>
    /// Plants a composed page directly into the cache — used to seed an entry whose <c>GeneratedAtUtc</c>
    /// is older than the fresh window, so a later fail-safe serve of it is age-detectable as stale.
    /// </summary>
    public async Task SeedProductPageAsync(Guid productId, ProductPageResponse page)
    {
        var cache = Services.GetRequiredService<IFusionCache>();
        await cache.SetAsync(BffCacheConstants.ProductPageKey(productId), page);
    }

    /// <summary>
    /// Marks the cached product page logically expired but still fail-safe-eligible — the trigger for a
    /// fail-safe stale serve (a later request with the gating upstream down serves this entry stale).
    /// </summary>
    public async Task ExpireProductPageAsync(Guid productId)
    {
        var cache = Services.GetRequiredService<IFusionCache>();
        await cache.ExpireAsync(BffCacheConstants.ProductPageKey(productId));
    }

    /// <summary>
    /// Wipes all upstream stubs (re-adding only the token endpoint) so a test can flip an upstream's
    /// health mid-run — e.g. cache a healthy page, then take Catalog down for the stale-serve path.
    /// </summary>
    public void ResetUpstreams()
    {
        _upstreams.Reset();
        StubTokenEndpoint();
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
                .WithBodyAsJson(new { access_token = "fake-service-token", expires_in = 300, token_type = "Bearer" }));

    private void StubGet(string path, int statusCode, object? body)
    {
        var response = Response.Create().WithStatusCode(statusCode);
        if (body is not null)
        {
            response = response
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(body);
        }

        _upstreams
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(response);
    }
}
