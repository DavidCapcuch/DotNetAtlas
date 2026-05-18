using Basket.Infrastructure.Common.Config;
using Basket.Infrastructure.Persistence;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Basket.UnitTests.Baskets.Persistence;

/// <summary>
/// Pins the failure contract of <see cref="RedisBasketRepository.DeleteAsync"/>: any
/// transient Redis exception MUST surface as <c>Result.Fail</c> so the post-checkout
/// fan-out in <c>CheckoutBasketCommandHandler</c> can honour its "outbox is source of
/// truth; delete failure logs and continues" promise (XML doc lines 33-35).
/// </summary>
public class RedisBasketRepositoryDeleteAsyncTests
{
    private readonly IFusionCacheProvider _cacheProvider = Substitute.For<IFusionCacheProvider>();
    private readonly IFusionCache _cache = Substitute.For<IFusionCache>();
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly FakeTimeProvider _timeProvider = new();

    public RedisBasketRepositoryDeleteAsyncTests()
    {
        _cacheProvider.GetCache(RedisBasketRepository.BasketCacheName).Returns(_cache);
        _multiplexer.GetDatabase().Returns(_database);
    }

    private RedisBasketRepository CreateSut() => new(
        _cacheProvider,
        _multiplexer,
        Options.Create(new BasketRedisOptions()),
        _timeProvider,
        NullLogger<RedisBasketRepository>.Instance);

    [Fact]
    public async Task DeleteAsync_WhenRedisThrowsRedisTimeoutException_ReturnsResultFail()
    {
        var userId = Guid.CreateVersion7();
        _database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsyncForAnyArgs(new RedisTimeoutException("simulated timeout", CommandStatus.Unknown));

        var result = await CreateSut().DeleteAsync(userId, TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task DeleteAsync_WhenRedisThrowsRedisConnectionException_ReturnsResultFail()
    {
        var userId = Guid.CreateVersion7();
        _database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsyncForAnyArgs(new RedisConnectionException(ConnectionFailureType.SocketFailure, "simulated"));

        var result = await CreateSut().DeleteAsync(userId, TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task DeleteAsync_WhenFusionCacheRemoveThrows_ReturnsResultFail()
    {
        // The repository's second hop (FusionCache backplane invalidation) must also
        // not leak — otherwise a thrown RedisException after the KeyDelete succeeded
        // would still escape the handler with a 5xx after the outbox row landed.
        var userId = Guid.CreateVersion7();
        _database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(true));
#pragma warning disable CA2012 // ValueTask returned by NSubstitute's When-Do setup is part of the mock plumbing, not awaited code.
        _cache.When(c => c.RemoveAsync(Arg.Any<string>(), Arg.Any<FusionCacheEntryOptions>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new RedisTimeoutException("simulated backplane timeout", CommandStatus.Unknown));
#pragma warning restore CA2012

        var result = await CreateSut().DeleteAsync(userId, TestContext.Current.CancellationToken);

        result.Should().BeFailure();
    }

    [Fact]
    public async Task DeleteAsync_WhenBothCallsSucceed_ReturnsResultOk()
    {
        var userId = Guid.CreateVersion7();
        _database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(true));

        var result = await CreateSut().DeleteAsync(userId, TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
    }
}
