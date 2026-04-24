using Platform.SharedKernel.Base;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.Common.ValueObjects;

/// <summary>
/// Per-rate VAT breakdown aggregated from the invoice's lines (e.g., <c>21% \u2192 \u20ac42</c>).
/// Immutable; computed at issuance and frozen on the aggregate.
/// </summary>
/// <param name="Rate">Applicable VAT rate (e.g., 21%).</param>
/// <param name="Base">Taxable subtotal at this rate.</param>
/// <param name="Amount">Tax amount (<see cref="Base"/> \u00d7 <see cref="Rate"/> / 100).</param>
public sealed record VatLine(VatRate Rate, Money Base, Money Amount) : ValueObject;
