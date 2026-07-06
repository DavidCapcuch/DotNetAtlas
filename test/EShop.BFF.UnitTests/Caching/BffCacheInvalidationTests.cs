using EShop.BFF.Infrastructure.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.UnitTests.Caching;

/// <summary>
/// The request-side best-effort invalidation a basket mutation triggers (bff.md § 3.6). The mutation has
/// <b>already committed</b> in Basket, so the eviction is post-commit: it must run to completion even when
/// the triggering request aborts (no cancellation token in the contract), and a transient
/// <c>redis-cache</c> fault must be swallowed — never propagated — or coherence maintenance would turn a
/// succeeded write into a 5xx. (The healthy path is covered end-to-end by <c>BasketMutationTests</c>; these
/// isolate the fault + cancellation contract.)
/// </summary>
public sealed class BffCacheInvalidationTests
{
    private const string Tag = "basket-bff-abc";

    [Fact]
    public async Task TryRemoveByTagAsync_WhenHealthy_RemovesTheTagWithAnUnabortableToken()
    {
        // Arrange
        var cache = Substitute.For<IFusionCache>();

        // Act
        await BffCacheInvalidation.TryRemoveByTagAsync(cache, Tag, NullLogger.Instance);

        // Assert — post-commit: the eviction is never wired to a request-abort token, so a client that
        // disconnects right after Basket committed cannot leave the stale page cached.
        await cache.Received(1).RemoveByTagAsync(Tag, Arg.Any<FusionCacheEntryOptions>(), CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task TryRemoveByTagAsync_WhenCacheFaults_SwallowsAndDoesNotThrow()
    {
        // Arrange — the volatile redis-cache is misbehaving.
        var cache = Substitute.For<IFusionCache>();

        // CA2012: NSubstitute consumes the configured ValueTask via interception to stub the call.
#pragma warning disable CA2012
        cache
            .RemoveByTagAsync(Tag, Arg.Any<FusionCacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("redis-cache down")));
#pragma warning restore CA2012

        // Act
        var act = async () => await BffCacheInvalidation.TryRemoveByTagAsync(cache, Tag, NullLogger.Instance);

        // Assert
        await act.Should().NotThrowAsync("a committed mutation must not 5xx on a cache hiccup");
    }

    [Fact]
    [Trait("Category", "resilience")]
    public async Task TryRemoveByTagAsync_WhenCacheCancelsInternally_SwallowsAsAFault()
    {
        // Arrange — with no caller token in the contract, a cancellation surfacing from the cache's own
        // internals (its soft timeouts) is just another transient fault, not a caller signal.
        var cache = Substitute.For<IFusionCache>();

        // CA2012: NSubstitute consumes the configured ValueTask via interception to stub the call.
#pragma warning disable CA2012
        cache
            .RemoveByTagAsync(Tag, Arg.Any<FusionCacheEntryOptions>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new OperationCanceledException()));
#pragma warning restore CA2012

        // Act
        var act = async () => await BffCacheInvalidation.TryRemoveByTagAsync(cache, Tag, NullLogger.Instance);

        // Assert
        await act.Should().NotThrowAsync("post-commit eviction has no caller cancellation to propagate");
    }
}
