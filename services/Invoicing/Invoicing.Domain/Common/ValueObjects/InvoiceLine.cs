using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.Common.ValueObjects;

/// <summary>
/// One line on an invoice / credit note. Immutable; frozen at issuance (I-2 non-empty; I-4 immutable).
/// </summary>
/// <remarks>
/// For credit notes, quantities remain positive but <see cref="LineTotal"/> and
/// <see cref="UnitPrice"/> carry the opposite sign (constructed via primary ctor, bypassing
/// <see cref="Money.Create"/>'s positivity check since the domain intent is known).
/// </remarks>
/// <param name="LineNumber">Position on the document (1-based).</param>
/// <param name="Sku">Product identifier snapshot from Catalog.</param>
/// <param name="Description">Human-readable line description.</param>
/// <param name="Quantity">Units on this line (always &gt; 0).</param>
/// <param name="UnitPrice">Price per unit; positive on invoice, negative on credit note.</param>
/// <param name="LineTotal">Line total (<see cref="UnitPrice"/> \u00d7 <see cref="Quantity"/>).</param>
/// <param name="VatRate">Applicable VAT rate.</param>
public sealed record InvoiceLine(
    int LineNumber,
    Sku Sku,
    string Description,
    int Quantity,
    Money UnitPrice,
    Money LineTotal,
    VatRate VatRate) : ValueObject
{
    public const int MaxDescriptionLength = 500;

    /// <summary>
    /// Creates a validated <see cref="InvoiceLine"/> for use on an <c>Invoice</c> aggregate
    /// (all amounts strictly positive; <see cref="LineTotal"/> == <see cref="UnitPrice"/>
    /// \u00d7 <see cref="Quantity"/>).
    /// </summary>
    public static Result<InvoiceLine> Create(
        int lineNumber,
        Sku sku,
        string description,
        int quantity,
        Money unitPrice,
        VatRate vatRate)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(unitPrice);
        ArgumentNullException.ThrowIfNull(vatRate);

        if (lineNumber < 1)
        {
            return Result.Fail<InvoiceLine>(new ValidationError(
                nameof(lineNumber), "LineNumber must be >= 1.", "Invoicing.InvalidLineNumber"));
        }

        if (string.IsNullOrWhiteSpace(description) || description.Length > MaxDescriptionLength)
        {
            return Result.Fail<InvoiceLine>(new ValidationError(
                nameof(description),
                $"Description is required and must be \u2264 {MaxDescriptionLength} chars.",
                "Invoicing.InvalidLineDescription"));
        }

        if (quantity < 1)
        {
            return Result.Fail<InvoiceLine>(new ValidationError(
                nameof(quantity), "Quantity must be >= 1.", "Invoicing.InvalidLineQuantity"));
        }

        var lineTotal = new Money(unitPrice.Amount * quantity, unitPrice.Currency);
        return Result.Ok(new InvoiceLine(lineNumber, sku, description.Trim(), quantity, unitPrice, lineTotal, vatRate));
    }

    /// <summary>
    /// Produces the mirror of this line for a credit note (signs flipped on both
    /// <see cref="UnitPrice"/> and <see cref="LineTotal"/>). Uses the primary <see cref="Money"/>
    /// constructor to bypass the positivity check \u2014 intent is explicit.
    /// </summary>
    public InvoiceLine WithFlippedSign()
    {
        var flippedUnitPrice = new Money(-UnitPrice.Amount, UnitPrice.Currency);
        var flippedLineTotal = new Money(-LineTotal.Amount, LineTotal.Currency);
        return new InvoiceLine(LineNumber, Sku, Description, Quantity, flippedUnitPrice, flippedLineTotal, VatRate);
    }
}
