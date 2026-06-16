using EShop.BFF.IntegrationTests.Common;

namespace EShop.BFF.IntegrationTests.HomePage;

/// <summary>
/// The eager-warm hosted service with <c>bff.home-page-eager-cache-warm = on</c> (issue #328): the
/// background warmer pre-populates <c>home-page:v1</c> just after startup (off the host-readiness path).
/// </summary>
[Collection<HomePageWarmOnCollection>]
public sealed class HomePageWarmOnTests(HomePageWarmOnFixture fixture)
{
    [Fact]
    public async Task EagerWarmOn_WarmsTheHomePageCacheAfterStartup()
    {
        // The warmer runs as a BackgroundService (off the startup path), so poll until it has composed.
        var warmed = await EventuallyTrueAsync(fixture.IsHomePageCachedAsync, TimeSpan.FromSeconds(20));

        using var _ = new AssertionScope();
        warmed.Should().BeTrue("the startup warmer should populate home-page:v1");
        fixture.CountCatalogSearchCalls().Should().BeGreaterThan(0, "warming composes from Catalog search");
    }

    private static async Task<bool> EventuallyTrueAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        return await condition();
    }
}

/// <summary>
/// The eager-warm hosted service with <c>bff.home-page-eager-cache-warm = off</c> (issue #328): the
/// background warmer skips cleanly — no warm, no upstream calls, no half-baked cache state.
/// </summary>
[Collection<HomePageWarmOffCollection>]
public sealed class HomePageWarmOffTests(HomePageWarmOffFixture fixture)
{
    [Fact]
    public async Task EagerWarmOff_SkipsTheWarmCleanly()
    {
        // Let the background warmer run (and skip), then confirm it neither warmed nor called upstream.
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        using var _ = new AssertionScope();
        (await fixture.IsHomePageCachedAsync()).Should().BeFalse("the warmer must skip when the flag is off");
        fixture.CountCatalogSearchCalls().Should().Be(0, "a skipped warm makes no upstream calls");
    }
}
