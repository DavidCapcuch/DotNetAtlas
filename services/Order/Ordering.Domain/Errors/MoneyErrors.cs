using DotNetAtlas.SharedKernel.Errors;

namespace Ordering.Domain.Errors;

/// <summary>
/// Validation errors for the <see cref="ValueObjects.Money"/> value object.
/// </summary>
public static class MoneyErrors
{
    public static ValidationError AmountMustBePositive()
        => new(
            propertyName: "Amount",
            errorMessage: "Amount must be greater than zero.",
            errorCode: "Money.AmountMustBePositive");

    public static ValidationError InvalidCurrencyCode()
        => new(
            propertyName: "Currency",
            errorMessage: "Currency must be a valid ISO 4217 currency code.",
            errorCode: "Money.InvalidCurrencyCode");
}

