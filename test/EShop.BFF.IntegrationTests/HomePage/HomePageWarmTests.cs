using EShop.BFF.IntegrationTests.Common;
using Platform.Test.Framework.Common;

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

        using (new AssertionScope())
        {
            warmed.Should().BeTrue("the startup warmer should populate home-page:v1");
            fixture.CountCatalogSearchCalls().Should().BeGreaterThan(0, "warming composes from Catalog search");
        }
    }

    private static async Task<bool> EventuallyTrueAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        try
        {
            await Eventually.UntilAsync(
                _ => condition(),
                timeout,
                "the background warmer to populate the home-page cache",
                TestContext.Current.CancellationToken);

            return true;
        }
        catch (EventuallyTimeoutException)
        {
            // Reported as a value rather than rethrown: the caller asserts on this inside an
            // AssertionScope, and an escaping exception would abort before the paired assertion
            // on the upstream call count ran — losing half the diagnostic the scope exists for.
            return false;
        }
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

        using (new AssertionScope())
        {
            (await fixture.IsHomePageCachedAsync()).Should().BeFalse("the warmer must skip when the flag is off");
            fixture.CountCatalogSearchCalls().Should().Be(0, "a skipped warm makes no upstream calls");
        }
    }
}
