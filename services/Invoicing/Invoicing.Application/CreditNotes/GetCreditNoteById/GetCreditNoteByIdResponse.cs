using Invoicing.Application.Invoices.GetInvoiceById;

namespace Invoicing.Application.CreditNotes.GetCreditNoteById;

/// <summary>
/// Read-side projection of a <c>CreditNote</c>. Reuses
/// <see cref="InvoiceLineDto"/> from the invoice projection so credit-note line items
/// (sign-flipped) and invoice line items render identically client-side.
/// </summary>
public sealed class GetCreditNoteByIdResponse
{
    public required Guid CreditNoteId { get; init; }

    /// <summary><c>CN-YYYY-NNNNNN</c> per ADR-0018.</summary>
    public required string CreditNoteNumber { get; init; }

    public required Guid OriginalInvoiceId { get; init; }

    /// <summary>The reversed invoice's <c>INV-YYYY-NNNNNN</c> number.</summary>
    public required string OriginalInvoiceNumber { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset IssueDate { get; init; }

    public DateTimeOffset? DeliveredAtUtc { get; init; }

    public required string Reason { get; init; }

    /// <summary>Always strictly negative per invariant I-CN-2.</summary>
    public required decimal TotalAmount { get; init; }

    public required string Currency { get; init; }

    public required IReadOnlyList<InvoiceLineDto> Lines { get; init; }

    /// <summary>Freshly-minted SAS URL to the credit-note PDF (10-minute TTL).</summary>
    public Uri? PdfPresignedUrl { get; init; }

    /// <summary>UTC instant the <see cref="PdfPresignedUrl"/> expires.</summary>
    public DateTimeOffset? PdfPresignedUrlExpiresAtUtc { get; init; }
}
