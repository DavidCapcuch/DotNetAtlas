using Invoicing.Domain.Invoices;

namespace Invoicing.Application.Invoices.GetInvoiceById;

/// <summary>
/// Projects an <see cref="Invoice"/> aggregate to the flat read-side DTO. Shared by
/// <see cref="GetInvoiceByIdQueryHandler"/>,
/// <see cref="GetInvoicesByBuyer.GetInvoicesByBuyerQueryHandler"/>, and
/// <see cref="GetInvoiceByOrderId.GetInvoiceByOrderIdQueryHandler"/> so all three
/// queries return byte-identical projections of the same invoice.
/// </summary>
/// <remarks>
/// SAS URL minting is the caller's responsibility — the projection takes the pre-minted
/// URI + expiry as parameters because <see cref="Invoicing.Application.Blobs.IBlobStore"/>
/// is async and cannot be awaited inside a static mapper.
/// </remarks>
internal static class InvoiceProjection
{
    public static GetInvoiceByIdResponse ToResponse(Invoice invoice, Uri? pdfPresignedUrl, DateTimeOffset? pdfExpiresAtUtc) =>
        new()
        {
            InvoiceId = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber?.Value,
            BuyerId = invoice.BuyerId,
            OrderId = invoice.OrderId,
            PaymentId = invoice.PaymentId,
            CorrelationId = invoice.CorrelationId,
            Status = invoice.Status.Name,
            IssueDate = invoice.IssueDate,
            DeliveredAtUtc = invoice.DeliveredAtUtc,
            DeliveryChannel = invoice.DeliveryChannel.Name,
            SubtotalAmount = invoice.Subtotal.Amount,
            TotalAmount = invoice.Total.Amount,
            Currency = invoice.Total.Currency.Name,
            Lines = [.. invoice.Lines.Select(l => new InvoiceLineDto(
                l.LineNumber,
                l.Sku.Value,
                l.Description,
                l.Quantity,
                l.UnitPrice.Amount,
                l.LineTotal.Amount,
                l.VatRate.Percentage))],
            VatLines = [.. invoice.VatLines.Select(v => new VatLineDto(
                v.Rate.Percentage,
                v.Base.Amount,
                v.Amount.Amount))],
            BillingAddress = new AddressDto(
                invoice.BillingAddress.Street1,
                invoice.BillingAddress.Street2,
                invoice.BillingAddress.City,
                invoice.BillingAddress.State,
                invoice.BillingAddress.PostalCode,
                invoice.BillingAddress.CountryCode),
            Cancellation = invoice.CancellationInfo is null
                ? null
                : new InvoiceCancellationDto(
                    invoice.CancellationInfo.CancelledAtUtc,
                    invoice.CancellationInfo.Reason.Name,
                    invoice.CancellationInfo.CreditNoteId),
            PdfPresignedUrl = pdfPresignedUrl,
            PdfPresignedUrlExpiresAtUtc = pdfExpiresAtUtc,
        };
}
