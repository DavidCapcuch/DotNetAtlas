using Platform.SharedKernel.Base;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.Common.ValueObjects;

/// <summary>
/// Per-rate VAT breakdown aggregated from the invoice's lines (e.g., <c>21% → €42</c>).
/// Immutable; computed at issuance and frozen on the aggregate.
/// </summary>
/// <remarks>
/// Authored as a non-positional record so EF Core can materialise it through the parameterless
/// constructor and set the owned <see cref="Money"/> navigations via <c>private init</c>
/// setters. Positional records emit a primary constructor with the owned navigations as
/// parameters, which EF rejects (owned navigations cannot be bound to constructor parameters).
/// </remarks>
public sealed record VatLine : ValueObject
{
    /// <summary>Applicable VAT rate (e.g., 21%).</summary>
    public VatRate Rate { get; private init; } = null!;

    /// <summary>Taxable subtotal at this rate.</summary>
    public Money Base { get; private init; } = null!;

    /// <summary>Tax amount (<see cref="Base"/> × <see cref="Rate"/> / 100).</summary>
    public Money Amount { get; private init; } = null!;

    // EF Core materialisation ctor.
    private VatLine()
    {
    }

    public VatLine(VatRate rate, Money @base, Money amount)
    {
        ArgumentNullException.ThrowIfNull(rate);
        ArgumentNullException.ThrowIfNull(@base);
        ArgumentNullException.ThrowIfNull(amount);

        Rate = rate;
        Base = @base;
        Amount = amount;
    }
}
