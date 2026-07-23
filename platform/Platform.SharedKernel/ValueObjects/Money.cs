using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Platform.SharedKernel.ValueObjects;

/// <summary>
/// Monetary amount in a specific ISO 4217 currency.
/// Shared-kernel value object (ADR-0036) — BCs use this directly instead of
/// defining their own. Immutable, self-validating, equality-by-value.
/// </summary>
/// <remarks>
/// Money is a currency-tagged signed decimal. Sign carries no intrinsic meaning at this
/// layer — positivity is an aggregate-level invariant enforced by the BC that uses Money
/// (e.g. <c>Product.Price > 0</c>, <c>OrderItem.UnitPrice > 0</c>, <c>PaymentTransaction.Amount.Amount > 0</c>).
/// A credit-note line legitimately holds negative Money; an invoice line legitimately holds
/// positive Money. Both are valid Money values.
/// </remarks>
public sealed record Money : ValueObject
{
    /// <summary>Amount (signed).</summary>
    public decimal Amount { get; private init; }

    /// <summary>ISO 4217 currency code.</summary>
    public CurrencyCode Currency { get; private init; } = null!;

    // Sole construction path is via Create / Zero / Negate / arithmetic.
    private Money()
    {
    }

    /// <summary>
    /// Creates a <see cref="Money"/>. Validates only the currency — amount sign is not
    /// constrained here; the holding aggregate enforces sign invariants where appropriate.
    /// </summary>
    /// <param name="amount">Amount (any sign).</param>
    /// <param name="currency">Currency code.</param>
    /// <returns>A successful result with the value.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="currency"/> is <c>null</c>.</exception>
    public static Result<Money> Create(decimal amount, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return Result.Ok(new Money { Amount = amount, Currency = currency });
    }

    /// <summary>
    /// Creates a <see cref="Money"/> from a three-letter ISO 4217 code.
    /// </summary>
    /// <param name="amount">Amount (any sign).</param>
    /// <param name="currencyCode">ISO 4217 three-letter code (case-insensitive).</param>
    /// <returns>A successful result with the value, or a failure result with a <see cref="ValidationError"/>.</returns>
    public static Result<Money> Create(decimal amount, string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Length != 3)
        {
            return Result.Fail<Money>(new ValidationError(
                nameof(Currency), "Currency must be a 3-letter ISO 4217 code.", "Money.InvalidCurrencyCode"));
        }

        if (!CurrencyCode.TryFromName(currencyCode.ToUpperInvariant(), out var currency))
        {
            return Result.Fail<Money>(new ValidationError(
                nameof(Currency), $"Unknown ISO 4217 currency code '{currencyCode}'.", "Money.UnknownCurrencyCode"));
        }

        return Create(amount, currency);
    }

    /// <summary>
    /// Returns a zero-amount <see cref="Money"/> in the given currency. Intent-revealing
    /// sugar for sites where the literal 0 carries domain meaning (e.g. a zero-rate VAT line
    /// amount, or the identity element for aggregation seeds).
    /// </summary>
    public static Money Zero(CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money { Amount = 0m, Currency = currency };
    }

    /// <summary>
    /// Returns the additive inverse — same currency, flipped sign. Used by credit-note
    /// reversal flows where an existing (positive) Money becomes its negative counterpart.
    /// </summary>
    public Money Negate() => new() { Amount = -Amount, Currency = Currency };

    /// <summary>
    /// Returns a Money with the same currency and a new amount. Non-failing — the currency is
    /// carried over unchanged (nothing is parsed, unlike <c>Create</c>), so it returns a bare
    /// Money. Used by reprice-style flows that change an amount within a fixed currency.
    /// </summary>
    public Money WithAmount(decimal amount) => new() { Amount = amount, Currency = Currency };

    /// <summary>
    /// Addition in the same currency.
    /// </summary>
    /// <param name="other">Other value.</param>
    /// <returns>Sum.</returns>
    /// <exception cref="InvalidOperationException">If currencies differ.</exception>
    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);
        return new Money { Amount = Amount + other.Amount, Currency = Currency };
    }

    /// <summary>
    /// Subtraction in the same currency.
    /// </summary>
    /// <param name="other">Other value.</param>
    /// <returns>Difference.</returns>
    /// <exception cref="InvalidOperationException">If currencies differ.</exception>
    public Money Subtract(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);
        return new Money { Amount = Amount - other.Amount, Currency = Currency };
    }

    /// <summary>Addition operator (same currency).</summary>
    public static Money operator +(Money left, Money right) => left.Add(right);

    /// <summary>Subtraction operator (same currency).</summary>
    public static Money operator -(Money left, Money right) => left.Subtract(right);

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException(
                $"Currency mismatch: '{Currency.Name}' vs '{other.Currency.Name}'.");
        }
    }
}
