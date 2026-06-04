using System.Linq.Expressions;
using Invoicing.Application.Invoices.GetInvoiceById;
using Invoicing.Domain.CreditNotes;

namespace Invoicing.Application.CreditNotes.GetCreditNoteById;

/// <summary>
/// SQL-side projection target for <see cref="GetCreditNoteByIdQueryHandler"/>. Mirrors
/// <see cref="InvoiceRow"/> for the credit-note path (ADR-0021 / #277). Carries every
/// <see cref="GetCreditNoteByIdResponse"/> field plus <see cref="PdfBlobName"/> — the one
/// column NOT in the response but needed to decide whether to mint a SAS URL after
/// materialisation.
/// </summary>
internal sealed record CreditNoteRow(
    Guid CreditNoteId,
    string? CreditNoteNumber,
    Guid OriginalInvoiceId,
    string OriginalInvoiceNumber,
    Guid BuyerId,
    string Status,
    DateTimeOffset IssueDate,
    DateTimeOffset? DeliveredAtUtc,
    string Reason,
    decimal TotalAmount,
    string Currency,
    IReadOnlyList<InvoiceLineDto> Lines,
    string? PdfBlobName)
{
    /// <summary>
    /// EF-translatable projection. Owned-collection <c>cn.Lines</c> and the nullable
    /// <c>cn.CreditNoteNumber</c> VO translate cleanly under EF Core 10.
    /// </summary>
    public static Expression<Func<CreditNote, CreditNoteRow>> Projection => cn => new CreditNoteRow(
        CreditNoteId: cn.Id,
        CreditNoteNumber: cn.CreditNoteNumber == null ? null : cn.CreditNoteNumber.Value,
        OriginalInvoiceId: cn.OriginalInvoiceId,
        OriginalInvoiceNumber: cn.OriginalInvoiceNumber.Value,
        BuyerId: cn.BuyerId,
        Status: cn.Status.Name,
        IssueDate: cn.IssueDate,
        DeliveredAtUtc: cn.DeliveredAtUtc,
        Reason: cn.Reason.Name,
        TotalAmount: cn.Total.Amount,
        Currency: cn.Total.Currency.Name,
        Lines: cn.Lines.Select(l => new InvoiceLineDto(
            l.LineNumber,
            l.Sku.Value,
            l.Description,
            l.Quantity,
            l.UnitPrice.Amount,
            l.LineTotal.Amount,
            l.VatRate.Percentage)).ToList(),
        PdfBlobName: cn.PdfBlobRef == null ? null : cn.PdfBlobRef.BlobName);

    public GetCreditNoteByIdResponse ToResponse(Uri? pdfPresignedUrl, DateTimeOffset? pdfExpiresAtUtc) =>
        new()
        {
            CreditNoteId = CreditNoteId,
            // CreditNote.Status starts at Issued (no Draft state), so CreditNoteNumber is
            // always set on a persisted credit note. The original CreditNoteProjection
            // used the same null-forgiving access (creditNote.CreditNoteNumber!.Value).
            CreditNoteNumber = CreditNoteNumber!,
            OriginalInvoiceId = OriginalInvoiceId,
            OriginalInvoiceNumber = OriginalInvoiceNumber,
            BuyerId = BuyerId,
            Status = Status,
            IssueDate = IssueDate,
            DeliveredAtUtc = DeliveredAtUtc,
            Reason = Reason,
            TotalAmount = TotalAmount,
            Currency = Currency,
            Lines = Lines,
            PdfPresignedUrl = pdfPresignedUrl,
            PdfPresignedUrlExpiresAtUtc = pdfExpiresAtUtc,
        };
}
