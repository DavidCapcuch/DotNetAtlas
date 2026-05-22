using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.Invoices.Events;
using Invoicing.Invoices;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Platform.SharedKernel.ValueObjects;
using Riok.Mapperly.Abstractions;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Maps <see cref="InvoiceIssuedDomainEvent"/> to the external Avro
/// <see cref="InvoiceIssuedEvent"/>. Decimal conversions go through
/// <see cref="AvroDecimalExtensions.ToAvroDecimal"/> (scale must match the .avsc, otherwise
/// Avro serialisation throws on a scale mismatch).
/// </summary>
/// <remarks>
/// Two distinct scales: 4 for monetary amounts (Subtotal, Total, VAT base, VAT amount —
/// per the schema's <c>decimal(19,4)</c>) and 2 for VAT rate percentages (per
/// <c>decimal(5,2)</c> on <c>InvoiceVatLine.Rate</c>). VAT lines may legitimately be empty
/// (every line at 0%); the mapper emits an empty list rather than null in that case.
/// </remarks>
[Mapper]
public static partial class InvoiceIssuedMapper
{
    private const int MoneyScale = 4;
    private const int RateScale = 2;

    public static InvoiceIssuedEvent ToInvoiceIssuedEvent(this InvoiceIssuedDomainEvent source) =>
        new()
        {
            InvoiceId = source.InvoiceId,
            InvoiceNumber = source.InvoiceNumber.Value,
            BuyerId = source.BuyerId,
            OrderId = source.OrderId,
            PaymentId = source.PaymentId,
            CorrelationId = source.CorrelationId,
            IssueDate = source.IssueDate.UtcDateTime,
            BillingAddress = MapAddress(source.BillingAddress),
            Subtotal = source.Subtotal.Amount.ToAvroDecimal(MoneyScale),
            Total = source.Total.Amount.ToAvroDecimal(MoneyScale),
            Currency = source.Total.Currency.Name,
            VatLines = source.VatLines.Select(MapVatLine).ToList(),
            PdfBlobUri = source.PdfBlobRef.BlobName,
            PdfContentHash = source.PdfBlobRef.ContentHash,
            PdfSizeBytes = source.PdfBlobRef.SizeBytes,
            DeliveryChannel = source.DeliveryChannel.Name,
        };

    [UserMapping]
    private static InvoiceBillingAddress MapAddress(Address source) =>
        new()
        {
            Street1 = source.Street1,
            Street2 = source.Street2,
            City = source.City,
            State = source.State,
            PostalCode = source.PostalCode,
            CountryCode = source.CountryCode,
        };

    [UserMapping]
    private static InvoiceVatLine MapVatLine(VatLine source) =>
        new()
        {
            Rate = source.Rate.Percentage.ToAvroDecimal(RateScale),
            BaseAmount = source.Base.Amount.ToAvroDecimal(MoneyScale),
            Amount = source.Amount.Amount.ToAvroDecimal(MoneyScale),
        };
}
