using Platform.SharedKernel.Base;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.Common.ValueObjects;

/// <summary>
/// One line on a credit note — a correction, return, or goodwill gesture against a prior
/// <see cref="InvoiceLine"/>. Its lifecycle is inherently backward-looking: a credit-note line
/// exists only to justify a reversal of a previously-issued invoice line.
/// </summary>
/// <remarks>
/// <para>
/// Although <see cref="CreditNoteLine"/> and <see cref="InvoiceLine"/> currently share the same
/// structural fields, they represent two distinct domain concepts. <see cref="InvoiceLine"/>
/// captures a sale or delivery of service (forward-looking lifecycle: drafted → finalized → billed).
/// <see cref="CreditNoteLine"/> captures a correction against a prior transaction. DDD prioritises
/// semantic clarity over DRY — this is accidental, not conceptual, duplication. Keeping the
/// types isolated lets them evolve independently as business rules change (e.g. a future
/// <c>ReasonCode</c>, <c>OriginalLineRef</c>, or <c>RestockingFee</c> belongs only here).
/// </para>
/// <para>
/// Constructed exclusively via <see cref="FromInvoiceLine"/> — a credit-note line exists only
/// as the reversal of an existing invoice line.
/// </para>
/// </remarks>
public sealed record CreditNoteLine : ValueObject
{
    public const int MaxDescriptionLength = 500;

    /// <summary>Position on the credit note (1-based; mirrors the original invoice line's number).</summary>
    public int LineNumber { get; private init; }

    /// <summary>Product identifier snapshot from the reversed invoice line.</summary>
    public Sku Sku { get; private init; } = null!;

    /// <summary>Human-readable line description (copied verbatim from the source invoice line).</summary>
    public string Description { get; private init; } = null!;

    /// <summary>Units being credited. Always &gt; 0 — quantity itself never flips sign; sign lives on the amounts.</summary>
    public int Quantity { get; private init; }

    /// <summary>Per-unit credit amount; negative (mirror of the reversed invoice line's <see cref="InvoiceLine.UnitPrice"/>).</summary>
    public Money UnitPrice { get; private init; } = null!;

    /// <summary>Total credit on this line (<see cref="UnitPrice"/> × <see cref="Quantity"/>); negative.</summary>
    public Money LineTotal { get; private init; } = null!;

    /// <summary>VAT rate that applied to the original invoice line.</summary>
    public VatRate VatRate { get; private init; } = null!;

    // EF Core materialisation ctor.
    private CreditNoteLine()
    {
    }

    private CreditNoteLine(
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
    /// Builds the credit-note line that reverses an invoice line: flips the signs on
    /// <see cref="UnitPrice"/> and <see cref="LineTotal"/>; preserves <see cref="Quantity"/>,
    /// <see cref="Sku"/>, <see cref="Description"/>, <see cref="VatRate"/>,
    /// and <see cref="LineNumber"/>. Sole sanctioned construction path.
    /// </summary>
    public static CreditNoteLine FromInvoiceLine(InvoiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new CreditNoteLine(
            line.LineNumber,
            line.Sku,
            line.Description,
            line.Quantity,
            line.UnitPrice.Negate(),
            line.LineTotal.Negate(),
            line.VatRate);
    }
}
