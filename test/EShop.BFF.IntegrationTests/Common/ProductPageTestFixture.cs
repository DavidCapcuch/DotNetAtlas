using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Platform.Test.Framework.Redis;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EShop.BFF.IntegrationTests.Common;

internal sealed class ProductPageTestCollection : TestCollection<ProductPageTestFixture>;

/// <summary>
/// Boots the BFF over a real <c>redis-cache</c> Testcontainer with Catalog, Inventory, and the
/// Keycloak token endpoint faked by a single WireMock server. Exercises the real typed clients
/// (service-auth + resilience) and the real FusionCache — only the upstreams are stubbed. Each test
/// uses a fresh ProductId, so per-scenario stubs never collide and no per-test WireMock reset is needed.
/// </summary>
[DisableWafCache]
public sealed class ProductPageTestFixture : AppFixture<Program>
{
    private readonly RedisTestContainer _redisContainer = new();

    private WireMockServer _upstreams = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await _redisContainer.StartAsync();

        _upstreams = WireMockServer.Start();

        // ClientCredentialsTokenHandler fetches a service token before each upstream call
        // (ADR-0010); fake the Keycloak token endpoint so the real auth pipeline runs.
        _upstreams
            .Given(Request.Create()
                .WithPath("/realms/dotnetatlas/protocol/openid-connect/token")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new { access_token = "fake-service-token", expires_in = 300, token_type = "Bearer" }));
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
        a.UseEnvironment("Testing");
    }

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

    public async Task ResetFixtureStateAsync() => await _redisContainer.CleanDataAsync();

    protected override async ValueTask TearDownAsync()
    {
        _upstreams.Stop();
        _upstreams.Dispose();
        await _redisContainer.DisposeAsync();
    }

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
