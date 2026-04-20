using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Platform.SharedKernel.ValueObjects;

/// <summary>
/// Monetary amount in a specific ISO 4217 currency.
/// Shared-kernel value object (ADR-0015 / Wave 0 pin) — BCs use this directly instead of
/// defining their own. Immutable, self-validating, equality-by-value.
/// </summary>
/// <param name="Amount">Strictly positive amount.</param>
/// <param name="Currency">ISO 4217 currency code.</param>
public sealed record Money(decimal Amount, CurrencyCode Currency) : ValueObject
{
    /// <summary>
    /// Creates a <see cref="Money"/> with validation.
    /// </summary>
    /// <param name="amount">Amount (must be &gt; 0).</param>
    /// <param name="currency">Currency code.</param>
    /// <returns>A successful result with the value, or a failure result with a <see cref="ValidationError"/>.</returns>
    public static Result<Money> Create(decimal amount, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (amount <= 0)
        {
            return Result.Fail<Money>(new ValidationError(
                nameof(Amount), "Amount must be strictly positive.", "Money.AmountMustBePositive"));
        }

        return Result.Ok(new Money(amount, currency));
    }

    /// <summary>
    /// Creates a <see cref="Money"/> from a three-letter ISO 4217 code.
    /// </summary>
    /// <param name="amount">Amount (must be &gt; 0).</param>
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
    /// Addition in the same currency.
    /// </summary>
    /// <param name="other">Other value.</param>
    /// <returns>Sum.</returns>
    /// <exception cref="InvalidOperationException">If currencies differ.</exception>
    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
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
        return new Money(Amount - other.Amount, Currency);
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
