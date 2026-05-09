using FluentResults;
using Platform.SharedKernel.Errors;

namespace Invoicing.Domain.Common.Errors;

/// <summary>
/// User-actionable + feature-gate validation errors produced by the Invoicing BC,
/// per <c>error-taxonomy.md \u00a7 3.6</c>. Canonical factory methods \u2014 use these rather
/// than constructing <see cref="ValidationError"/> directly in command handlers.
/// </summary>
/// <remarks>
/// <para>
/// Bug-class integrity violations (e.g., <c>I-CN-1</c> \u2014 credit note against already-cancelled
/// invoice) are raised as <c>DataIntegrityException</c>, not returned as <see cref="Result"/>s.
/// See <see cref="TotalMismatchError"/> / <see cref="PdfGenerationFailedError"/> for
/// typed <see cref="IError"/> carriers used by the DLT pipeline.
/// </para>
/// </remarks>
public static class InvoicingErrors
{
    public static ValidationError InvoiceNotFound(Guid invoiceId) =>
        new("InvoiceId", $"Invoice '{invoiceId}' does not exist.", "Invoicing.InvoiceNotFound");

    /// <summary>
    /// Variant of <see cref="InvoiceNotFound"/> for the by-order lookup. Same error code
    /// (so HTTP mapping still routes to 404) but the property name + message correctly
    /// reflect the OrderId input rather than misleadingly claiming an Invoice with that
    /// GUID is missing.
    /// </summary>
    public static ValidationError InvoiceForOrderNotFound(Guid orderId) =>
        new("OrderId", $"No invoice exists for order '{orderId}'.", "Invoicing.InvoiceNotFound");

    public static ValidationError CreditNoteNotFound(Guid creditNoteId) =>
        new("CreditNoteId", $"Credit note '{creditNoteId}' does not exist.", "Invoicing.CreditNoteNotFound");

    public static ValidationError InvoiceAlreadyIssued(Guid correlationId) =>
        new(
            "CorrelationId",
            $"Invoice already issued for correlation '{correlationId}'.",
            "Invoicing.InvoiceAlreadyIssued");

    public static ValidationError PartialRefundNotSupportedV1() =>
        new(
            "Amount",
            "Partial refunds are not supported in v1; credit notes must be full-amount.",
            "Invoicing.PartialRefundNotSupportedV1");

    public static ValidationError BlobUploadFailed() =>
        new(
            "Blob",
            "Invoice PDF upload to object storage failed after retries.",
            "Invoicing.BlobUploadFailed");

    public static ValidationError CreditNoteRefersToCancelledInvoice(Guid invoiceId) =>
        new(
            "OriginalInvoiceId",
            $"Cannot issue a credit note against cancelled invoice '{invoiceId}' (I-CN-1).",
            "Invoicing.CreditNoteRefersToCancelledInvoice");

    public static ValidationError InvalidInvoiceTransition(string from, string to) =>
        new(
            "Status",
            $"Invalid invoice state transition: {from} \u2192 {to}.",
            "Invoicing.InvalidInvoiceTransition");

    public static ValidationError InvalidCreditNoteTransition(string from, string to) =>
        new(
            "Status",
            $"Invalid credit-note state transition: {from} \u2192 {to}.",
            "Invoicing.InvalidCreditNoteTransition");
}

/// <summary>
/// Bug-class error \u2014 surfaces through the DLT pipeline when the order total and the payment
/// amount disagree for the same <c>CorrelationId</c>. Example mapping 1.4.
/// </summary>
public sealed record TotalMismatchError(decimal OrderTotal, decimal PaymentAmount, Guid CorrelationId) : IError
{
    public string Message =>
        $"Total mismatch on correlation {CorrelationId}: order total {OrderTotal}, payment amount {PaymentAmount}.";

    public Dictionary<string, object> Metadata { get; } = new()
    {
        ["ErrorCode"] = "Invoicing.TotalMismatch",
    };

    public List<IError> Reasons { get; } = [];
}

/// <summary>
/// Bug-class error \u2014 PDF rendering failed (library bug or corrupt input). DLT'd and alerted.
/// </summary>
public sealed record PdfGenerationFailedError(string Detail) : IError
{
    public string Message => $"PDF generation failed: {Detail}";

    public Dictionary<string, object> Metadata { get; } = new()
    {
        ["ErrorCode"] = "Invoicing.PdfGenerationFailed",
    };

    public List<IError> Reasons { get; } = [];
}
