using FluentResults;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.Application.Abstractions;

/// <summary>
/// Persistence port for the Basket aggregate root. The implementation
/// (<c>RedisBasketRepository</c> in the Infrastructure layer) stores the aggregate
/// as a MemoryPack-serialized envelope on <c>redis-basket</c> (ADR-0016).
/// </summary>
/// <remarks>
/// <para>
/// Concurrency is optimistic. Callers load the aggregate via
/// <see cref="GetByUserIdAsync"/>, mutate it, and pass the originally-loaded
/// <c>Version</c> back to <see cref="SaveAsync"/> as <c>expectedVersion</c>. A
/// mismatch surfaces as <c>BasketConcurrencyError</c>; the command handler
/// retries exactly once per basket.md § 5.4 before propagating the failure.
/// </para>
/// <para>
/// <see cref="DeleteAsync"/> bypasses the FusionCache layer and issues a direct
/// Redis <c>DEL</c> so post-checkout cleanup is unambiguous (basket.md § 6.4).
/// </para>
/// </remarks>
public interface IBasketRepository
{
    /// <summary>
    /// Loads the basket for <paramref name="userId"/> from <c>redis-basket</c>.
    /// </summary>
    /// <param name="userId">The basket owner's identifier (the aggregate Id).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result.Ok{T}(T)"/> wrapping the aggregate, or <see cref="Result.Ok{T}(T)"/>
    /// of <see langword="null"/> when no entry exists for the user. Transport / serialization
    /// failures surface as <see cref="Result.Fail{T}(string)"/> with an infrastructure-class error.
    /// </returns>
    Task<Result<BasketAggregate?>> GetByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Persists <paramref name="basket"/> under optimistic concurrency.
    /// </summary>
    /// <param name="basket">The mutated aggregate. Its <c>Version</c> must already reflect the mutations applied (the domain increments it via <c>Touch()</c>).</param>
    /// <param name="expectedVersion">The <c>Version</c> the aggregate had when it was loaded — not its current value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result.Ok()"/> on success; <c>Result.Fail(BasketConcurrencyError)</c>
    /// when the value stored in Redis no longer matches <paramref name="expectedVersion"/>.
    /// </returns>
    Task<Result> SaveAsync(BasketAggregate basket, int expectedVersion, CancellationToken ct);

    /// <summary>
    /// Permanently removes the basket entry from <c>redis-basket</c>. Idempotent —
    /// succeeds (with no-op semantics) when the key is already absent. Used by the
    /// checkout flow after the outbox write commits.
    /// </summary>
    Task<Result> DeleteAsync(Guid userId, CancellationToken ct);
}
