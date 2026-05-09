using Invoicing.Application.Invoices.GetInvoiceById;
using Invoicing.Domain.CreditNotes;

namespace Invoicing.Application.CreditNotes.GetCreditNoteById;

/// <summary>
/// Projects a <see cref="CreditNote"/> aggregate to the flat read-side DTO. Caller mints
/// the SAS URL.
/// </summary>
internal static class CreditNoteProjection
{
    public static GetCreditNoteByIdResponse ToResponse(
        CreditNote creditNote,
        Uri? pdfPresignedUrl,
        DateTimeOffset? pdfExpiresAtUtc) =>
        new()
        {
            CreditNoteId = creditNote.Id,
            CreditNoteNumber = creditNote.CreditNoteNumber!.Value,
            OriginalInvoiceId = creditNote.OriginalInvoiceId,
            OriginalInvoiceNumber = creditNote.OriginalInvoiceNumber.Value,
            BuyerId = creditNote.BuyerId,
            CorrelationId = creditNote.CorrelationId,
            Status = creditNote.Status.Name,
            IssueDate = creditNote.IssueDate,
            DeliveredAtUtc = creditNote.DeliveredAtUtc,
            Reason = creditNote.Reason.Name,
            TotalAmount = creditNote.Total.Amount,
            Currency = creditNote.Total.Currency.Name,
            Lines = [.. creditNote.Lines.Select(l => new InvoiceLineDto(
                l.LineNumber,
                l.Sku.Value,
                l.Description,
                l.Quantity,
                l.UnitPrice.Amount,
                l.LineTotal.Amount,
                l.VatRate.Percentage))],
            PdfPresignedUrl = pdfPresignedUrl,
            PdfPresignedUrlExpiresAtUtc = pdfExpiresAtUtc,
        };
}
