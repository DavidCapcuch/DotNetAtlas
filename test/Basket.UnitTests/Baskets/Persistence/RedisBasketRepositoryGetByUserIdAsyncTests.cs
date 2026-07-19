using Basket.Infrastructure.Common.Config;
using Basket.Infrastructure.Persistence;
using Basket.Infrastructure.Persistence.Documents;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.SharedKernel.Errors;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Basket.UnitTests.Baskets.Persistence;

/// <summary>
/// Covers <see cref="RedisBasketRepository.GetByUserIdAsync"/>'s
/// transport / serialization-failure contract: per
/// <see cref="Basket.Application.Abstractions.IBasketRepository.GetByUserIdAsync"/>
/// XML doc, "Transport / serialization failures surface as Result.Fail." A currency
/// code not present in the <c>CurrencyCode</c> SmartEnum (e.g. removed after the basket
/// was persisted) is one such failure — it surfaces as <c>Result.Fail</c> rather than
/// throwing out of the mapper.
/// </summary>
public class RedisBasketRepositoryGetByUserIdAsyncTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 04, 25, 12, 00, 00, TimeSpan.Zero);

    private readonly IFusionCacheProvider _cacheProvider = Substitute.For<IFusionCacheProvider>();
    private readonly IFusionCache _cache = Substitute.For<IFusionCache>();
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    public RedisBasketRepositoryGetByUserIdAsyncTests()
    {
        _cacheProvider.GetCache(RedisBasketRepository.BasketCacheName).Returns(_cache);
    }

    private RedisBasketRepository CreateSut() => new(
        _cacheProvider,
        _multiplexer,
        Options.Create(new BasketRedisOptions()),
        _timeProvider,
        NullLogger<RedisBasketRepository>.Instance);

    [Fact]
    [Trait("Category", "regression")]
    public async Task GetByUserIdAsync_WhenStoredCurrencyIsUnknown_ReturnsCorruptionFailureNotThrow()
    {
        // sum2.H-4 regression guard. Pre-fix, an unknown currency code stored in
        // Redis (e.g. a currency removed from the CurrencyCode SmartEnum after a
        // user's basket was persisted) propagated SmartEnumNotFoundException out of
        // GetByUserIdAsync — which violated the documented Result.Fail contract and
        // bubbled as a 5xx to the caller for what is really a data-evolution issue.

        // Arrange
        var userId = Guid.CreateVersion7();
        var corruptedDoc = new BasketStateDocument(
            Version: 1,
            Payload: new BasketDocument(
                userId,
                new[]
                {
                    new BasketItemDocument(
                        Guid.CreateVersion7(),
                        new ProductSnapshotDocument(
                            "SKU-1",
                            "Widget",
                            10m,
                            "XYZ_NOT_A_REAL_CURRENCY",
                            Now),
                        Quantity: 1),
                },
                Now,
                Now));

        _cache.TryGetAsync<BasketStateDocument>(
            Arg.Any<string>(),
            Arg.Any<FusionCacheEntryOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(MaybeValue<BasketStateDocument>.FromValue(corruptedDoc));

        var sut = CreateSut();

        // Act
        var act = async () => await sut.GetByUserIdAsync(userId, TestContext.Current.CancellationToken);

        // Assert
        var result = await act.Should().NotThrowAsync();
        using (new AssertionScope())
        {
            result.Subject.Should().BeFailure();
            result.Subject.Errors[0].Should().BeOfType<ValidationError>()
                .Which.ErrorCode.Should().Be("Basket.Corruption");
        }
    }

    [Fact]
    public async Task GetByUserIdAsync_WhenStoredCurrencyIsKnown_ReturnsBasket()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var goodDoc = new BasketStateDocument(
            Version: 1,
            Payload: new BasketDocument(
                userId,
                new[]
                {
                    new BasketItemDocument(
                        Guid.CreateVersion7(),
                        new ProductSnapshotDocument("SKU-1", "Widget", 10m, "USD", Now),
                        Quantity: 1),
                },
                Now,
                Now));

        _cache.TryGetAsync<BasketStateDocument>(
            Arg.Any<string>(),
            Arg.Any<FusionCacheEntryOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(MaybeValue<BasketStateDocument>.FromValue(goodDoc));

        // Act
        var result = await CreateSut().GetByUserIdAsync(userId, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().NotBeNull();
            result.Value!.UserId.Should().Be(userId);
        }
    }
}
