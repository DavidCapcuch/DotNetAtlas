using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Invoicing.Domain.Common.ValueObjects;

/// <summary>
/// VAT rate expressed as a percentage in the closed range [0, 100], quantized to two decimals
/// (jurisdictional rates are always whole or half percent in practice; two-decimal precision
/// accommodates that without permitting absurd input).
/// </summary>
/// <remarks>
/// Not a SmartEnum \u2014 rates vary by jurisdiction and change over time. Invoicing records the
/// rate supplied by Ordering at checkout; it does not compute tax (<c>invoicing.md</c> \u00a7 17).
/// </remarks>
/// <param name="Percentage">Rate percentage in [0, 100], max 2 decimals.</param>
public sealed record VatRate(decimal Percentage) : ValueObject
{
    public static Result<VatRate> Create(decimal percentage)
    {
        if (percentage < 0m || percentage > 100m)
        {
            return Result.Fail<VatRate>(new ValidationError(
                nameof(Percentage), "VAT rate must be between 0 and 100 (inclusive).", "Invoicing.InvalidVatRate"));
        }

        // Quantize to two decimals (reject inputs like 19.995) — fail-fast on precision surprises.
        var scaled = decimal.Round(percentage, 2, MidpointRounding.ToEven);
        if (scaled != percentage)
        {
            return Result.Fail<VatRate>(new ValidationError(
                nameof(Percentage), "VAT rate must have at most 2 decimals.", "Invoicing.InvalidVatRatePrecision"));
        }

        return Result.Ok(new VatRate(percentage));
    }

    public override string ToString() => $"{Percentage}%";
}
