using Invoicing.Domain.Common.ValueObjects;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.Invoices.ValueObjects;

/// <summary>
/// Frozen capture of an <see cref="Invoice"/>'s state at the moment a reversal is initiated.
/// Carried into <see cref="CreditNotes.CreditNote.Create"/> so the credit-note aggregate never
/// holds a direct reference to the Invoice aggregate (DDD aggregate boundary; mirrors
/// <c>Basket.Domain.Baskets.ValueObjects.ProductSnapshot</c> and
/// <c>Ordering.Domain.Baskets.BasketSnapshot</c>).
/// </summary>
/// <remarks>
/// Constructed only via <see cref="Invoice.ToReversalSnapshot"/>; that factory enforces the
/// "Issued or Delivered + has InvoiceNumber" precondition so the snapshot is always
/// reversal-eligible by construction. <see cref="ReversalLines"/> is pre-flipped — line totals
/// and unit prices already have their signs inverted.
/// </remarks>
public sealed record InvoiceSnapshot : ValueObject
{
    /// <summary>Identifier of the source <see cref="Invoice"/>.</summary>
    public Guid InvoiceId { get; private init; }

    /// <summary>Gap-free <see cref="InvoiceNumber"/> stamped on the source invoice.</summary>
    public InvoiceNumber InvoiceNumber { get; private init; } = null!;

    /// <summary>Buyer the source invoice was issued to.</summary>
    public Guid BuyerId { get; private init; }

    /// <summary>Sign-flipped copies of the source invoice's lines as <see cref="CreditNoteLine"/>s,
    /// ready to store on a credit note.</summary>
    public IReadOnlyList<CreditNoteLine> ReversalLines { get; private init; } = [];

    /// <summary>Source invoice total at the moment of capture (positive; credit note inverts).</summary>
    public Money Total { get; private init; } = null!;

    /// <summary>UTC instant the snapshot was taken; explicit time-anchor for audit reconstruction.</summary>
    public DateTimeOffset CapturedAtUtc { get; private init; }

    private InvoiceSnapshot()
    {
    }

    internal static InvoiceSnapshot Create(
        Guid invoiceId,
        InvoiceNumber invoiceNumber,
        Guid buyerId,
        IReadOnlyList<CreditNoteLine> reversalLines,
        Money total,
        DateTimeOffset capturedAtUtc) =>
        new()
        {
            InvoiceId = invoiceId,
            InvoiceNumber = invoiceNumber,
            BuyerId = buyerId,
            ReversalLines = reversalLines,
            Total = total,
            CapturedAtUtc = capturedAtUtc,
        };
}
