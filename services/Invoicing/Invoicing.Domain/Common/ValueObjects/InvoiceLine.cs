using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.Common.ValueObjects;

/// <summary>
/// One line on an invoice — a sale or delivery of service. Its lifecycle moves forward:
/// drafted, finalized, billed. Amounts are strictly positive (<see cref="UnitPrice"/>,
/// <see cref="LineTotal"/>); the credit-note reversal is a distinct type
/// <see cref="CreditNoteLine"/> with its own backward-looking lifecycle.
/// Immutable; frozen at issuance (I-2 non-empty; I-4 immutable).
/// </summary>
/// <remarks>
/// Authored as a non-positional record so EF Core can materialise it through the parameterless
/// constructor and set the owned <see cref="Money"/> navigations via <c>private init</c>
/// setters. Positional records emit a primary constructor whose owned-navigation parameters
/// EF rejects.
/// </remarks>
public sealed record InvoiceLine : ValueObject
{
    public const int MaxDescriptionLength = 500;

    /// <summary>Position on the document (1-based).</summary>
    public int LineNumber { get; private init; }

    /// <summary>Product identifier snapshot from Catalog.</summary>
    public Sku Sku { get; private init; } = null!;

    /// <summary>Human-readable line description.</summary>
    public string Description { get; private init; } = null!;

    /// <summary>Units on this line (always &gt; 0).</summary>
    public int Quantity { get; private init; }

    /// <summary>Price per unit; strictly positive on an invoice.</summary>
    public Money UnitPrice { get; private init; } = null!;

    /// <summary>Line total (<see cref="UnitPrice"/> × <see cref="Quantity"/>); strictly positive.</summary>
    public Money LineTotal { get; private init; } = null!;

    /// <summary>Applicable VAT rate.</summary>
    public VatRate VatRate { get; private init; } = null!;

    // EF Core materialisation ctor.
    private InvoiceLine()
    {
    }

    private InvoiceLine(
        int lineNumber,
        Sku sku,
        string description,
        int quantity,
        Money unitPrice,
        Money lineTotal,
        VatRate vatRate)
    {
        LineNumber = lineNumber;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = lineTotal;
        VatRate = vatRate;
    }

    /// <summary>
    /// Creates a validated <see cref="InvoiceLine"/> for use on an <c>Invoice</c> aggregate.
    /// All amounts strictly positive; <see cref="LineTotal"/> == <see cref="UnitPrice"/>
    /// × <see cref="Quantity"/>.
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
                $"Description is required and must be ≤ {MaxDescriptionLength} chars.",
                "Invoicing.InvalidLineDescription"));
        }

        if (quantity < 1)
        {
            return Result.Fail<InvoiceLine>(new ValidationError(
                nameof(quantity), "Quantity must be >= 1.", "Invoicing.InvalidLineQuantity"));
        }

        if (unitPrice.Amount <= 0)
        {
            return Result.Fail<InvoiceLine>(new ValidationError(
                nameof(unitPrice),
                "UnitPrice must be strictly positive on an invoice line; credit-note reversals use CreditNoteLine.",
                "Invoicing.InvoiceLineUnitPriceMustBePositive"));
        }

        var lineTotal = Money.Create(unitPrice.Amount * quantity, unitPrice.Currency).Value;
        return Result.Ok(new InvoiceLine(lineNumber, sku, description.Trim(), quantity, unitPrice, lineTotal, vatRate));
    }
}
