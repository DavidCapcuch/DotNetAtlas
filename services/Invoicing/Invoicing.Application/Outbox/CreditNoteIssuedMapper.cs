using Invoicing.CreditNotes;
using Invoicing.Domain.CreditNotes.Events;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Maps <see cref="CreditNoteIssuedDomainEvent"/> to the external Avro
/// <see cref="CreditNoteIssuedEvent"/>. <c>Total</c> is intentionally negative (Invariant
/// I-CN-2); the conversion preserves the sign through <c>ToAvroDecimal</c>.
/// </summary>
internal static class CreditNoteIssuedMapper
{
    private const int MoneyScale = 4;

    public static CreditNoteIssuedEvent ToCreditNoteIssuedEvent(this CreditNoteIssuedDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new CreditNoteIssuedEvent
        {
            CreditNoteId = source.CreditNoteId,
            CreditNoteNumber = source.CreditNoteNumber.Value,
            OriginalInvoiceId = source.OriginalInvoiceId,
            OriginalInvoiceNumber = source.OriginalInvoiceNumber.Value,
            BuyerId = source.BuyerId,
            IssueDate = source.IssueDate.UtcDateTime,
            Total = source.Total.Amount.ToAvroDecimal(MoneyScale),
            Currency = source.Total.Currency.Name,
            Reason = source.Reason.Name,
            PdfBlobName = source.PdfBlobRef.BlobName,
            PdfContentHash = source.PdfBlobRef.ContentHash,
            PdfSizeBytes = source.PdfBlobRef.SizeBytes,
            OccurredOnUtc = source.OccurredOnUtc.UtcDateTime,
        };
    }
}
