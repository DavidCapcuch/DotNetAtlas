using Basket.Application.Abstractions;
using Basket.Domain.Baskets.Errors;
using Basket.Infrastructure.Common.Config;
using Basket.Infrastructure.Persistence.Documents;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Infrastructure.Persistence;

/// <summary>
/// Redis-backed implementation of <see cref="IBasketRepository"/> following
/// basket.md &#xa7; 5.4. Reads and versioned writes go through the named
/// <c>"basket"</c> FusionCache (distributed cache on <c>redis-basket</c> with
/// MemoryPack serialization); a per-user Redis lock wraps the load-check-write
/// sequence to give CAS semantics on top of the non-atomic pair. Post-checkout
/// <see cref="DeleteAsync"/> bypasses FusionCache and issues a direct
/// <c>DEL</c> so the intent is unambiguous (basket.md &#xa7; 6.4).
/// </summary>
internal sealed class RedisBasketRepository : IBasketRepository
{
    internal const string BasketKeyPrefix = "basket:";
    internal const string LockKeyPrefix = "basket-lock:";
    internal const string BasketCacheName = "basket";

    // Atomic lock release: compare stored token to our token and DEL only on match,
    // so we never release a lock we do not own (e.g. when our own wait exceeded the
    // lock TTL and another writer has taken over).
    private const string ReleaseLockScript = @"
if redis.call('get', KEYS[1]) == ARGV[1] then
    return redis.call('del', KEYS[1])
else
    return 0
end";

    private readonly IFusionCache _cache;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly BasketRedisOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RedisBasketRepository> _logger;

    public RedisBasketRepository(
        IFusionCacheProvider cacheProvider,
        [FromKeyedServices(BasketCacheName)] IConnectionMultiplexer multiplexer,
        IOptions<BasketRedisOptions> options,
        TimeProvider timeProvider,
        ILogger<RedisBasketRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(cacheProvider);
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _cache = cacheProvider.GetCache(BasketCacheName);
        _multiplexer = multiplexer;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<BasketAggregate?>> GetByUserIdAsync(Guid userId, CancellationToken ct)
    {
        var key = BasketKey(userId);
        var maybe = await _cache.TryGetAsync<BasketStateDocument>(key, token: ct).ConfigureAwait(false);
        if (!maybe.HasValue)
        {
            return Result.Ok<BasketAggregate?>(null);
        }

        try
        {
            return Result.Ok<BasketAggregate?>(BasketStateMapper.ToDomain(maybe.Value));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The mapper calls CurrencyCode.FromName(..., ignoreCase: false) which
            // throws SmartEnumNotFoundException if the persisted currency code is no
            // longer recognised (e.g. the SmartEnum entry was removed after a user's
            // basket was persisted). Per IBasketRepository.GetByUserIdAsync's contract,
            // transport / serialization failures surface as Result.Fail — not as an
            // unhandled exception bubbling 5xx out of every read for that user.
            _logger.LogError(
                ex,
                "Failed to rehydrate basket for user {UserId} from Redis payload; treating as corruption.",
                userId);
            return Result.Fail<BasketAggregate?>(BasketErrors.Corruption(userId));
        }
    }

    public async Task<Result> SaveAsync(BasketAggregate basket, int expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(basket);

        var key = BasketKey(basket.UserId);
        var lockKey = LockKey(basket.UserId);
        var lockToken = Guid.NewGuid().ToString("N");
        var lockTtl = TimeSpan.FromSeconds(_options.LockTimeoutSeconds);

        if (!await TryAcquireLockAsync(lockKey, lockToken, lockTtl, ct).ConfigureAwait(false))
        {
            _logger.LogWarning(
                "Basket lock acquisition timed out for user {UserId} after {Retries} retries",
                basket.UserId,
                _options.LockMaxRetries);
            return Result.Fail(new BasketConcurrencyError(basket.UserId, expectedVersion, -1));
        }

        try
        {
            var maybeCurrent = await _cache.TryGetAsync<BasketStateDocument>(key, token: ct).ConfigureAwait(false);
            var currentVersion = maybeCurrent.HasValue ? maybeCurrent.Value.Version : 0;

            // expectedVersion == 0 is the "no basket existed when loaded" case; the key must still be absent.
            // expectedVersion > 0 is the "loaded at version N" case; the key must exist and still report N.
            var expectedKeyExists = expectedVersion > 0;
            if (maybeCurrent.HasValue != expectedKeyExists || currentVersion != expectedVersion)
            {
                return Result.Fail(new BasketConcurrencyError(basket.UserId, expectedVersion, currentVersion));
            }

            var document = BasketStateMapper.ToDocument(basket);
            await _cache.SetAsync(
                key,
                document,
                options => options
                    .SetDuration(TimeSpan.FromDays(_options.TtlDays))
                    .SetSkipMemoryCache(true),
                token: ct).ConfigureAwait(false);

            return Result.Ok();
        }
        finally
        {
            await ReleaseLockAsync(lockKey, lockToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Permanently removes the basket entry for <paramref name="userId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two writes hit Redis in sequence: a direct <c>DEL</c> on the
    /// <c>basket:{userId}</c> key (bypassing FusionCache because checkout's
    /// intent is unambiguous), and a <c>FusionCache.RemoveAsync</c> that
    /// publishes a backplane invalidation so other Basket.Api instances drop
    /// any cached read of the same key. The second call is NOT redundant for
    /// data removal — it's the backplane signal — and must not be "simplified
    /// away" by a future refactor that sees a duplicate DEL.
    /// </para>
    /// <para>
    /// <see cref="SaveAsync"/>'s per-user CAS lock is deliberately NOT acquired
    /// here. By design (checkout is terminal — the basket is being torn down),
    /// the cost of acquiring the lock outweighs the rare race window: a
    /// concurrent in-flight <see cref="SaveAsync"/> whose write lands AFTER
    /// this delete will leave a phantom basket key at the old version+1.
    /// Documented as a known race in basket.md § 6.4. The 30-day TTL reclaims
    /// the phantom; the next user mutation discovers the inconsistency.
    /// </para>
    /// </remarks>
    public async Task<Result> DeleteAsync(Guid userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var key = BasketKey(userId);
        var database = _multiplexer.GetDatabase();

        // Catch transient Redis failures: the handler's checkout flow commits the
        // outbox BEFORE calling DeleteAsync, so a thrown exception here would surface
        // as 5xx to the caller while the saga is already running. Per
        // IBasketRepository.DeleteAsync's contract + CheckoutBasketCommandHandler XML
        // doc lines 33-35: "delete failure is logged but NOT propagated — the outbox
        // is the source of truth".
        try
        {
            await database.KeyDeleteAsync(key).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            await _cache.RemoveAsync(key, token: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // RedisTimeoutException : TimeoutException (NOT RedisException), so the
            // when-clause covers both StackExchange.Redis exception roots in one catch.
            _logger.LogWarning(
                ex,
                "Redis delete failed for basket key {Key}; outbox is the source of truth, next checkout or TTL will reclaim.",
                key);
            return Result.Fail($"Redis delete failed for basket '{userId:D}': {ex.Message}");
        }

        return Result.Ok();
    }

    internal static string BasketKey(Guid userId) => $"{BasketKeyPrefix}{userId:D}";

    internal static string LockKey(Guid userId) => $"{LockKeyPrefix}{userId:D}";

    private async Task<bool> TryAcquireLockAsync(string lockKey, string lockToken, TimeSpan lockTtl, CancellationToken ct)
    {
        var database = _multiplexer.GetDatabase();
        var retryDelay = TimeSpan.FromMilliseconds(_options.LockRetryDelayMs);

        for (var attempt = 0; attempt <= _options.LockMaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var acquired = await database
                .StringSetAsync(lockKey, lockToken, lockTtl, when: When.NotExists)
                .ConfigureAwait(false);
            if (acquired)
            {
                return true;
            }

            if (attempt == _options.LockMaxRetries)
            {
                return false;
            }

            await Task.Delay(retryDelay, _timeProvider, ct).ConfigureAwait(false);
        }

        return false;
    }

    private async Task ReleaseLockAsync(string lockKey, string lockToken)
    {
        try
        {
            var database = _multiplexer.GetDatabase();
            // ScriptEvaluateAsync intentionally drops the caller's CancellationToken: this
            // runs from a `finally` block and a cancelled release would leave the lock
            // held until its TTL expires, blocking OTHER users' writes for up to 5 s.
            // Best-effort fire-and-respect-Redis-side-timeout is the right trade-off here.
            await database.ScriptEvaluateAsync(
                ReleaseLockScript,
                keys: [lockKey],
                values: [lockToken])
                .ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            // Best-effort release — the lock's TTL will expire it if we fail. Log and swallow
            // so we never mask the caller's primary SaveAsync result (success or concurrency error).
            _logger.LogWarning(ex, "Basket lock release failed for key {LockKey}; TTL will reclaim it.", lockKey);
        }
    }
}
