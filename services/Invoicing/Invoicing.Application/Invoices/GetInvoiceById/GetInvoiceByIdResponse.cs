namespace Invoicing.Application.Invoices.GetInvoiceById;

/// <summary>
/// Read-side projection of an <c>Invoice</c>. Flat DTO — deliberately avoids exposing the
/// aggregate's mutation surface to the API layer. The PDF presigned URL is freshly minted
/// per request (10-minute TTL per ADR-0017) so the persisted SAS URL on
/// <c>PdfBlobRef</c> never escapes the Infrastructure layer.
/// </summary>
public sealed class GetInvoiceByIdResponse
{
    public required Guid InvoiceId { get; init; }

    /// <summary><c>INV-YYYY-NNNNNN</c> per ADR-0018; null while the invoice is still <c>Draft</c>.</summary>
    public string? InvoiceNumber { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset IssueDate { get; init; }

    public DateTimeOffset? DeliveredAtUtc { get; init; }

    public required string DeliveryChannel { get; init; }

    public required decimal SubtotalAmount { get; init; }

    public required decimal TotalAmount { get; init; }

    public required string Currency { get; init; }

    public required IReadOnlyList<InvoiceLineDto> Lines { get; init; }

    public required IReadOnlyList<VatLineDto> VatLines { get; init; }

    public required AddressDto BillingAddress { get; init; }

    public InvoiceCancellationDto? Cancellation { get; init; }

    /// <summary>Freshly-minted SAS URL to the PDF (10-minute TTL); null while the invoice is still <c>Draft</c>.</summary>
    public Uri? PdfPresignedUrl { get; init; }

    /// <summary>UTC instant the <see cref="PdfPresignedUrl"/> expires; null when no PDF.</summary>
    public DateTimeOffset? PdfPresignedUrlExpiresAtUtc { get; init; }
}

public sealed record InvoiceLineDto(
    int LineNumber,
    string Sku,
    string Description,
    int Quantity,
    decimal UnitPriceAmount,
    decimal LineTotalAmount,
    decimal VatRatePercentage);

public sealed record VatLineDto(
    decimal RatePercentage,
    decimal BaseAmount,
    decimal Amount);

public sealed record AddressDto(
    string Street1,
    string? Street2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);

public sealed record InvoiceCancellationDto(
    DateTimeOffset CancelledAtUtc,
    string Reason,
    Guid CreditNoteId);
