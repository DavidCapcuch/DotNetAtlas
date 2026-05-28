using Platform.SharedKernel.Errors;

namespace Invoicing.Domain.Common.Errors;

/// <summary>
/// User-actionable + feature-gate validation errors produced by the Invoicing BC,
/// per <c>error-taxonomy.md § 3.6</c>. Canonical factory methods — use these rather
/// than constructing <see cref="DomainError"/> subclasses directly in command handlers.
/// </summary>
/// <remarks>
/// <para>
/// Bug-class integrity violations (e.g., <c>I-CN-1</c> — credit note against already-cancelled
/// invoice) are raised as <c>DataIntegrityException</c>, not returned as
/// <see cref="FluentResults.Result"/>s. The Invoicing BC's typed bug-class exceptions live
/// in <c>Invoicing.Application.Common.Exceptions</c> (<c>InvoiceTotalMismatchException</c>,
/// <c>PdfGenerationFailedException</c>); both inherit <c>DataIntegrityException</c> so the
/// consumer middleware DLTs them via the existing <c>CriticalException</c> branch.
/// </para>
/// </remarks>
public static class InvoicingErrors
{
    public static NotFoundError InvoiceNotFound(Guid invoiceId) =>
        new("Invoice", invoiceId, "Invoicing.InvoiceNotFound");

    /// <summary>
    /// Variant of <see cref="InvoiceNotFound"/> for the by-order lookup. Same error code
    /// (so HTTP mapping still routes to 404) but the entity name reflects that the lookup
    /// was by Order rather than by Invoice id.
    /// </summary>
    public static NotFoundError InvoiceForOrderNotFound(Guid orderId) =>
        new("InvoiceForOrder", orderId, "Invoicing.InvoiceNotFound");

    public static NotFoundError CreditNoteNotFound(Guid creditNoteId) =>
        new("CreditNote", creditNoteId, "Invoicing.CreditNoteNotFound");

    public static ConflictError InvoiceAlreadyIssued(Guid correlationId) =>
        new(
            entityName: "Invoice",
            message: $"Invoice already issued for correlation '{correlationId}'.",
            errorCode: "Invoicing.InvoiceAlreadyIssued");

    public static NotImplementedError PartialRefundNotSupportedV1() =>
        new(
            featureName: "PartialRefund",
            message: "Partial refunds are not supported in v1; credit notes must be full-amount.",
            errorCode: "Invoicing.PartialRefundNotSupportedV1");

    public static ServiceUnavailableError BlobUploadFailed() =>
        new(
            resourceName: "InvoiceBlobStorage",
            message: "Invoice PDF upload to object storage failed after retries.",
            errorCode: "Invoicing.BlobUploadFailed");

    public static ConflictError CreditNoteRefersToCancelledInvoice(Guid invoiceId) =>
        new(
            entityName: "Invoice",
            message: $"Cannot issue a credit note against cancelled invoice '{invoiceId}' (I-CN-1).",
            errorCode: "Invoicing.CreditNoteRefersToCancelledInvoice");

    public static ConflictError InvalidInvoiceTransition(string from, string to) =>
        new(
            entityName: "Invoice",
            message: $"Invalid invoice state transition: {from} → {to}.",
            errorCode: "Invoicing.InvalidInvoiceTransition");

    public static ConflictError InvalidCreditNoteTransition(string from, string to) =>
        new(
            entityName: "CreditNote",
            message: $"Invalid credit-note state transition: {from} → {to}.",
            errorCode: "Invoicing.InvalidCreditNoteTransition");
}
