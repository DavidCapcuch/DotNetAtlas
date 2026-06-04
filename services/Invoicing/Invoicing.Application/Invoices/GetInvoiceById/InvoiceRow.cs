using System.Linq.Expressions;
using Invoicing.Domain.Invoices;

namespace Invoicing.Application.Invoices.GetInvoiceById;

/// <summary>
/// SQL-side projection target for the three invoice read handlers (GetInvoiceById,
/// GetInvoiceByOrderId, GetInvoicesByBuyer). Carries every <see cref="GetInvoiceByIdResponse"/>
/// field plus <see cref="PdfBlobName"/> — the one column NOT in the response but needed to
/// decide whether to mint a SAS URL after materialisation. Per ADR-0021, the projection lives
/// in the read-side handlers (no Ardalis.Specification) and travels to SQL via the
/// <see cref="Projection"/> expression.
/// </summary>
internal sealed record InvoiceRow(
    Guid InvoiceId,
    string? InvoiceNumber,
    Guid BuyerId,
    Guid OrderId,
    Guid PaymentId,
    string Status,
    DateTimeOffset IssueDate,
    DateTimeOffset? DeliveredAtUtc,
    string DeliveryChannel,
    decimal SubtotalAmount,
    decimal TotalAmount,
    string Currency,
    IReadOnlyList<InvoiceLineDto> Lines,
    IReadOnlyList<VatLineDto> VatLines,
    AddressDto BillingAddress,
    InvoiceCancellationDto? Cancellation,
    string? PdfBlobName)
{
    /// <summary>
    /// EF-translatable projection. Used by every read-side invoice handler so the three
    /// queries return byte-identical row shapes. Owned-collection
    /// (<c>i.Lines</c>/<c>i.VatLines</c>) <c>.Select(...).ToList()</c> and conditional
    /// nullable VO projection (<c>i.CancellationInfo == null ? null : new ...Dto(...)</c>)
    /// both translate cleanly under EF Core 10.
    /// </summary>
    public static Expression<Func<Invoice, InvoiceRow>> Projection => i => new InvoiceRow(
        InvoiceId: i.Id,
        InvoiceNumber: i.InvoiceNumber == null ? null : i.InvoiceNumber.Value,
        BuyerId: i.BuyerId,
        OrderId: i.OrderId,
        PaymentId: i.PaymentId,
        Status: i.Status.Name,
        IssueDate: i.IssueDate,
        DeliveredAtUtc: i.DeliveredAtUtc,
        DeliveryChannel: i.DeliveryChannel.Name,
        SubtotalAmount: i.Subtotal.Amount,
        TotalAmount: i.Total.Amount,
        Currency: i.Total.Currency.Name,
        Lines: i.Lines.Select(l => new InvoiceLineDto(
            l.LineNumber,
            l.Sku.Value,
            l.Description,
            l.Quantity,
            l.UnitPrice.Amount,
            l.LineTotal.Amount,
            l.VatRate.Percentage)).ToList(),
        VatLines: i.VatLines.Select(v => new VatLineDto(
            v.Rate.Percentage,
            v.Base.Amount,
            v.Amount.Amount)).ToList(),
        BillingAddress: new AddressDto(
            i.BillingAddress.Street1,
            i.BillingAddress.Street2,
            i.BillingAddress.City,
            i.BillingAddress.State,
            i.BillingAddress.PostalCode,
            i.BillingAddress.CountryCode),
        Cancellation: i.CancellationInfo == null
            ? null
            : new InvoiceCancellationDto(
                i.CancellationInfo.CancelledAtUtc,
                i.CancellationInfo.Reason.Name,
                i.CancellationInfo.CreditNoteId),
        PdfBlobName: i.PdfBlobRef == null ? null : i.PdfBlobRef.BlobName);

    public GetInvoiceByIdResponse ToResponse(Uri? pdfPresignedUrl, DateTimeOffset? pdfExpiresAtUtc) =>
        new()
        {
            InvoiceId = InvoiceId,
            InvoiceNumber = InvoiceNumber,
            BuyerId = BuyerId,
            OrderId = OrderId,
            PaymentId = PaymentId,
            Status = Status,
            IssueDate = IssueDate,
            DeliveredAtUtc = DeliveredAtUtc,
            DeliveryChannel = DeliveryChannel,
            SubtotalAmount = SubtotalAmount,
            TotalAmount = TotalAmount,
            Currency = Currency,
            Lines = Lines,
            VatLines = VatLines,
            BillingAddress = BillingAddress,
            Cancellation = Cancellation,
            PdfPresignedUrl = pdfPresignedUrl,
            PdfPresignedUrlExpiresAtUtc = pdfExpiresAtUtc,
        };
}
