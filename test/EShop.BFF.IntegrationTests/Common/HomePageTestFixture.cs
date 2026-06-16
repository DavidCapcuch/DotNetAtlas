using EShop.BFF.Api.Responses;
using EShop.BFF.Infrastructure.Caching;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.Test.Framework.Redis;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.IntegrationTests.Common;

internal sealed class HomePageTestCollection : TestCollection<HomePageTestFixture>;

/// <summary>
/// Boots the BFF over a real <c>redis-cache</c> Testcontainer with Catalog, Inventory, and the Keycloak
/// token endpoint faked by a single WireMock server. Exercises the real typed clients + the real
/// home-page FusionCache (key <c>home-page:v1</c>). The home page is a single shared cache key, so —
/// unlike the product page — each test resets WireMock and flushes redis (<see cref="ResetFixtureStateAsync"/>).
/// The eager-warm hosted service is disabled here (its own dedicated fixture exercises it) so each test
/// drives composition deterministically from an empty cache.
/// </summary>
[DisableWafCache]
public sealed class HomePageTestFixture : AppFixture<Program>
{
    private const string CatalogSearchPath = "/api/v1/catalog/products";
    private const string CategoryTreePath = "/api/v1/catalog/categories/tree";
    private const string InventoryBulkPath = "/api/v1/inventory/stock-items/bulk";

    private readonly RedisTestContainer _redisContainer = new();

    private WireMockServer _upstreams = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await _redisContainer.StartAsync();
        _upstreams = WireMockServer.Start();
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
                .UseSetting("ServiceAuth:Authority", $"{upstreamUrl}/realms/dotnetatlas")
                .UseSetting("ServiceAuth:ClientId", "bff")
                .UseSetting("ServiceAuth:ClientSecret", "test-secret")
                .UseSetting("ServiceAuth:ServiceName", "bff")
                .UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
        });

        return base.ConfigureAppHost(a);
    }

    // Warm off so composition tests start from an empty cache (the warmer doesn't pre-populate it).
    protected override void ConfigureApp(IWebHostBuilder a) =>
        a.UseEnvironment("Testing").UseTestSerilog().UseWarmFlag(enabled: false);

    /// <summary>Stubs Catalog's search <c>GET /api/v1/catalog/products</c> with a 200 paged body.</summary>
    public void StubCatalogSearch(object body) => StubGet(CatalogSearchPath, 200, body);

    /// <summary>Stubs Catalog's search with a bare status code (e.g. 500).</summary>
    public void StubCatalogSearchStatus(int statusCode) => StubGet(CatalogSearchPath, statusCode, body: null);

    /// <summary>Stubs Catalog's <c>GET /api/v1/catalog/categories/tree</c> with a 200 body.</summary>
    public void StubCategoryTree(object body) => StubGet(CategoryTreePath, 200, body);

    /// <summary>Stubs Catalog's category tree with a bare status code (e.g. 500).</summary>
    public void StubCategoryTreeStatus(int statusCode) => StubGet(CategoryTreePath, statusCode, body: null);

    /// <summary>Stubs Inventory's bulk <c>POST /api/v1/inventory/stock-items/bulk</c> with a 200 body.</summary>
    public void StubInventoryBulk(object body) => StubPost(InventoryBulkPath, 200, body);

    /// <summary>Stubs Inventory's bulk read with a bare status code (e.g. 500).</summary>
    public void StubInventoryBulkStatus(int statusCode) => StubPost(InventoryBulkPath, statusCode, body: null);

    /// <summary>Number of times an upstream path was called (proves cache hits avoid the upstream).</summary>
    public int CountCatalogSearchCalls() =>
        _upstreams.FindLogEntries(Request.Create().WithPath(CatalogSearchPath).UsingGet()).Count;

    /// <summary>
    /// Removes the <c>home-page</c> tag — the exact production invalidation path the bff-group consumer
    /// runs. A composed entry must carry this tag, so this evicts it and the next request re-composes.
    /// </summary>
    public async Task RemoveHomePageTagAsync()
    {
        var cache = Services.GetRequiredService<IFusionCache>();
        await cache.RemoveByTagAsync(BffCacheConstants.HomePageTag);
    }

    /// <summary>
    /// Plants a composed page directly into the cache — used to seed an entry whose <c>GeneratedAtUtc</c>
    /// is older than the fresh window, so a later fail-safe serve of it is age-detectable as stale.
    /// </summary>
    public async Task SeedHomePageAsync(HomePageResponse page)
    {
        var cache = Services.GetRequiredService<IFusionCache>();
        await cache.SetAsync(BffCacheConstants.HomePageKey, page, BffHomePageCache.EntryOptions());
    }

    /// <summary>
    /// Marks the cached home page logically expired but still fail-safe-eligible — the trigger for a
    /// fail-safe stale serve (a later request with Catalog search down serves this entry stale).
    /// </summary>
    public async Task ExpireHomePageAsync()
    {
        var cache = Services.GetRequiredService<IFusionCache>();
        await cache.ExpireAsync(BffCacheConstants.HomePageKey);
    }

    /// <summary>
    /// Wipes all upstream stubs (re-adding only the token endpoint) so a test can flip an upstream's
    /// health mid-run — e.g. cache a healthy page, then take Catalog search down for the stale-serve path.
    /// </summary>
    public void ResetUpstreams()
    {
        _upstreams.Reset();
        StubTokenEndpoint();
    }

    public async Task ResetFixtureStateAsync()
    {
        ResetUpstreams();

        // The home page is one constant key, so flushing redis (L2) alone leaves the in-process L1 entry
        // behind. A tag/soft removal would also leave a fail-safe stale shadow a later test could serve,
        // so hard-clear the whole cache (allowFailSafe: false) — each test starts from a truly empty cache.
        var cache = Services.GetRequiredService<IFusionCache>();
        await cache.ClearAsync(allowFailSafe: false);
        await _redisContainer.CleanDataAsync();
    }

    protected override async ValueTask TearDownAsync()
    {
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

    private void StubGet(string path, int statusCode, object? body) =>
        _upstreams
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(BuildResponse(statusCode, body));

    private void StubPost(string path, int statusCode, object? body) =>
        _upstreams
            .Given(Request.Create().WithPath(path).UsingPost())
            .RespondWith(BuildResponse(statusCode, body));

    private static IResponseBuilder BuildResponse(int statusCode, object? body)
    {
        var response = Response.Create().WithStatusCode(statusCode);
        return body is null
            ? response
            : response.WithHeader("Content-Type", "application/json").WithBodyAsJson(body);
    }
}
