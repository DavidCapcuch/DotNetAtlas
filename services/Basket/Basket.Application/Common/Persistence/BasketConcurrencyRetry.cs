using Basket.Domain.Baskets.Errors;
using FluentResults;

namespace Basket.Application.Common.Persistence;

/// <summary>
/// Shared helper that retries a basket mutation exactly once when the first attempt
/// fails with <see cref="BasketConcurrencyError"/>. Keeps the five mutating command
/// handlers (AddItem, RemoveItem, ChangeItemQuantity, RefreshPrices, Clear) DRY and
/// aligned with the single-retry policy described in <c>basket.md § 5.4</c>.
/// </summary>
/// <remarks>
/// Each <c>attempt</c> delegate MUST perform a fresh load, apply its mutation, and call
/// <see cref="Abstractions.IBasketRepository.SaveAsync"/>. Any error other than
/// <see cref="BasketConcurrencyError"/> propagates immediately (no retry) — for example
/// domain-rule failures from the aggregate such as <c>BasketErrors.MaxItemsReached</c>.
/// </remarks>
public static class BasketConcurrencyRetry
{
    /// <summary>
    /// Executes <paramref name="attempt"/>, retrying once on
    /// <see cref="BasketConcurrencyError"/>.
    /// </summary>
    public static async Task<Result> ExecuteAsync(
        Func<CancellationToken, Task<Result>> attempt,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var first = await attempt(ct);
        if (first.IsSuccess || !first.HasError<BasketConcurrencyError>())
        {
            return first;
        }

        return await attempt(ct);
    }

    /// <summary>
    /// Executes <paramref name="attempt"/>, retrying once on
    /// <see cref="BasketConcurrencyError"/>. Generic overload for handlers that return
    /// a value (e.g., <c>CheckoutBasketCommand</c> returns the correlation id).
    /// </summary>
    public static async Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<Result<T>>> attempt,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var first = await attempt(ct);
        if (first.IsSuccess || !first.HasError<BasketConcurrencyError>())
        {
            return first;
        }

        return await attempt(ct);
    }
}
