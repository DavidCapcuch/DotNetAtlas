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

/// <summary>
/// Boots the BFF over a real redis-cache Testcontainer with Catalog + Inventory faked by WireMock, with
/// upstream stubs in place <em>before</em> the host starts so the startup <c>HomePageCacheWarmer</c> can
/// compose against them. The warmer's <c>StartAsync</c> awaits the warm, so by the time the fixture is
/// ready the warm has finished (or been skipped) — tests assert the resulting cache state directly. The
/// <see cref="FlagFilePath"/> (flag on vs off) is the only difference between the two concrete fixtures.
/// </summary>
[DisableWafCache]
public abstract class HomePageWarmFixtureBase : AppFixture<Program>
{
    private const string CatalogSearchPath = "/api/v1/catalog/products";
    private const string CategoryTreePath = "/api/v1/catalog/categories/tree";
    private const string InventoryBulkPath = "/api/v1/inventory/stock-items/bulk";

    private readonly RedisTestContainer _redisContainer = new();
    private WireMockServer _upstreams = null!;

    /// <summary>Whether <c>bff.home-page-eager-cache-warm</c> resolves on (warm runs) or off (skipped).</summary>
    protected abstract bool WarmEnabled { get; }

    protected override async ValueTask PreSetupAsync()
    {
        await _redisContainer.StartAsync();
        _upstreams = WireMockServer.Start();

        StubPost("/realms/dotnetatlas/protocol/openid-connect/token", new
        {
            access_token = "fake-service-token",
            expires_in = 300,
            token_type = "Bearer",
        });
        StubGet(CatalogSearchPath, new
        {
            total = 1,
            pageNumber = 1,
            pageSize = 20,
            items = new[]
            {
                new
                {
                    productId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    sku = "SKU-1",
                    name = "Laptop",
                    categoryBreadcrumb = "Electronics",
                    brandName = "Acme",
                    price = new { amount = 9.99m, currency = "USD" },
                    status = "Active",
                    primaryImageUrl = (string?)null,
                },
            },
        });
        StubGet(CategoryTreePath, new { nodes = Array.Empty<object>() });
        StubPost(InventoryBulkPath, new { items = Array.Empty<object>(), missingProductIds = Array.Empty<Guid>() });
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

    protected override void ConfigureApp(IWebHostBuilder a) =>
        a.UseEnvironment("Testing").UseTestSerilog().UseWarmFlag(WarmEnabled);

    public async Task<bool> IsHomePageCachedAsync()
    {
        var cache = Services.GetRequiredService<IFusionCache>();
        var maybe = await cache.TryGetAsync<HomePageResponse>(BffCacheConstants.HomePageKey);
        return maybe.HasValue;
    }

    /// <summary>How many times Catalog search was called — 0 proves the warm was skipped.</summary>
    public int CountCatalogSearchCalls() =>
        _upstreams.FindLogEntries(Request.Create().WithPath(CatalogSearchPath).UsingGet()).Count;

    protected override async ValueTask TearDownAsync()
    {
        _upstreams.Stop();
        _upstreams.Dispose();
        await _redisContainer.DisposeAsync();
    }

    private void StubGet(string path, object body) =>
        _upstreams
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(JsonResponse(body));

    private void StubPost(string path, object body) =>
        _upstreams
            .Given(Request.Create().WithPath(path).UsingPost())
            .RespondWith(JsonResponse(body));

    private static IResponseBuilder JsonResponse(object body) =>
        Response.Create()
            .WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBodyAsJson(body);
}

/// <summary><c>bff.home-page-eager-cache-warm</c> = on — the warmer pre-warms the home page on startup.</summary>
public sealed class HomePageWarmOnFixture : HomePageWarmFixtureBase
{
    protected override bool WarmEnabled => true;
}

/// <summary><c>bff.home-page-eager-cache-warm</c> = off — the warmer skips cleanly on startup.</summary>
public sealed class HomePageWarmOffFixture : HomePageWarmFixtureBase
{
    protected override bool WarmEnabled => false;
}

internal sealed class HomePageWarmOnCollection : TestCollection<HomePageWarmOnFixture>;

internal sealed class HomePageWarmOffCollection : TestCollection<HomePageWarmOffFixture>;
