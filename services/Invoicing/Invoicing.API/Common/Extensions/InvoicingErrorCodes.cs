namespace Invoicing.API.Common.Extensions;

/// <summary>
/// Stringly-typed mirror of the <c>errorCode</c> values produced by
/// <c>Invoicing.Domain.Common.Errors.InvoicingErrors</c>. Lives in the API layer because
/// the mapping it drives — error-code → HTTP status — is an HTTP concern; the Domain
/// layer's error factories do not (and must not) know about HTTP status codes.
/// </summary>
/// <remarks>
/// Keep these constants in sync with
/// <c>services/Invoicing/Invoicing.Domain/Common/Errors/InvoicingErrors.cs</c>. M9's
/// architecture-test slice will pin a one-shot reflection assertion: every constant
/// here must correspond to a factory method on <c>InvoicingErrors</c>.
/// </remarks>
internal static class InvoicingErrorCodes
{
    /// <summary>Maps to HTTP 404 Not Found.</summary>
    public const string InvoiceNotFound = "Invoicing.InvoiceNotFound";

    /// <summary>Maps to HTTP 404 Not Found.</summary>
    public const string CreditNoteNotFound = "Invoicing.CreditNoteNotFound";

    /// <summary>Maps to HTTP 409 Conflict (FSM rejection — invoice not in a resendable state).</summary>
    public const string InvalidInvoiceTransition = "Invoicing.InvalidInvoiceTransition";

    /// <summary>Maps to HTTP 409 Conflict (idempotent re-issue attempt).</summary>
    public const string InvoiceAlreadyIssued = "Invoicing.InvoiceAlreadyIssued";

    /// <summary>Maps to HTTP 501 Not Implemented (v1 feature gate).</summary>
    public const string PartialRefundNotSupportedV1 = "Invoicing.PartialRefundNotSupportedV1";
}
