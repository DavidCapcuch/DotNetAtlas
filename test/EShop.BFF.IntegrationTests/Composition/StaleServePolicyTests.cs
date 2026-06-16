using EShop.BFF.Api.Composition;

namespace EShop.BFF.IntegrationTests.Composition;

/// <summary>
/// Boundary coverage for fail-safe stale-serve detection (bff.md § 3.1 / § 3.4): a page is treated as
/// served-stale only once it is strictly older than the fresh window (soft TTL + jitter), the oldest a
/// still-fresh cache entry can be. Pure logic, so it is verified directly (no host / cache).
/// </summary>
public sealed class StaleServePolicyTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 06, 16, 12, 00, 00, TimeSpan.Zero);
    private static readonly TimeSpan FreshWindow = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30);

    [Fact]
    public void WasServedStale_WhenPageWithinFreshWindow_IsFalse() =>
        StaleServePolicy.WasServedStale(GeneratedAt, GeneratedAt.AddMinutes(5), FreshWindow)
            .Should().BeFalse();

    [Fact]
    public void WasServedStale_AtExactFreshWindowBoundary_IsFalse() =>
        StaleServePolicy.WasServedStale(GeneratedAt, GeneratedAt.Add(FreshWindow), FreshWindow)
            .Should().BeFalse();

    [Fact]
    public void WasServedStale_WhenPageOlderThanFreshWindow_IsTrue() =>
        StaleServePolicy.WasServedStale(GeneratedAt, GeneratedAt.Add(FreshWindow).AddSeconds(1), FreshWindow)
            .Should().BeTrue();
}
