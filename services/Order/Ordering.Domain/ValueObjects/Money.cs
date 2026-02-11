using DotNetAtlas.SharedKernel.Base;
using FluentResults;
using Ordering.Domain.Errors;

namespace Ordering.Domain.ValueObjects;

/// <summary>
/// Value object representing a monetary amount with its ISO 4217 currency.
/// Immutable and validated on creation. Amount must be strictly positive.
/// </summary>
/// <remarks>
/// Supports <c>+</c> and <c>-</c> operators with same-currency invariant.
/// Use <see cref="Create"/> factory method to construct validated instances.
/// </remarks>
public sealed record Money : ValueObject
{
    /// <summary>
    /// The monetary amount. Always greater than zero.
    /// </summary>
    public decimal Amount { get; private init; }

    /// <summary>
    /// The ISO 4217 currency code.
    /// </summary>
    public CurrencyCode Currency { get; private init; }

    /// <summary>
    /// EF Core parameterless constructor.
    /// </summary>
    private Money()
    {
    }

    /// <summary>
    /// Creates a new <see cref="Money"/> instance with the specified amount and currency.
    /// </summary>
    /// <param name="amount">The monetary amount. Must be greater than zero.</param>
    /// <param name="currency">The ISO 4217 currency code. Must be a defined <see cref="CurrencyCode"/> value.</param>
    /// <returns>A result containing the <see cref="Money"/> or validation errors.</returns>
    public static Result<Money> Create(decimal amount, CurrencyCode currency)
    {
        var mergedResults = Result.Merge(
            Result.FailIf(amount <= 0, MoneyErrors.AmountMustBePositive()),
            Result.FailIf(!Enum.IsDefined(currency), MoneyErrors.InvalidCurrencyCode()));

        if (mergedResults.IsFailed)
        {
            return mergedResults;
        }

        return new Money
        {
            Amount = amount,
            Currency = currency
        };
    }

    /// <summary>
    /// Adds two monetary values. Both operands must have the same currency.
    /// </summary>
    /// <param name="left">The first monetary value.</param>
    /// <param name="right">The second monetary value.</param>
    /// <returns>A new <see cref="Money"/> with the summed amount and the shared currency.</returns>
    /// <exception cref="InvalidOperationException">Thrown when currencies do not match.</exception>
    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);

        return new Money
        {
            Amount = left.Amount + right.Amount,
            Currency = left.Currency
        };
    }

    /// <summary>
    /// Subtracts one monetary value from another. Both operands must have the same currency.
    /// The result must be strictly positive.
    /// </summary>
    /// <param name="left">The monetary value to subtract from.</param>
    /// <param name="right">The monetary value to subtract.</param>
    /// <returns>A new <see cref="Money"/> with the difference and the shared currency.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when currencies do not match or when the result would be zero or negative.
    /// </exception>
    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);

        var result = left.Amount - right.Amount;
        if (result <= 0)
        {
            throw new InvalidOperationException(
                $"Subtraction would result in a non-positive amount: {result} {left.Currency}.");
        }

        return new Money
        {
            Amount = result,
            Currency = left.Currency
        };
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"{Amount} {Currency.ToString().ToUpperInvariant()}";

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot perform arithmetic on money with different currencies: " +
                $"{left.Currency} and {right.Currency}.");
        }
    }
}

