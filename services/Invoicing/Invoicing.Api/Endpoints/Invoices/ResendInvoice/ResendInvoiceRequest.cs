using FastEndpoints;

namespace Invoicing.Api.Endpoints.Invoices.ResendInvoice;

/// <summary>
/// HTTP request shape for <c>POST /api/v1/invoicing/invoices/{invoiceId}/resend</c>. The
/// admin authorisation is derived from the JWT realm role (<c>AuthPolicies.InvoicingAdmin</c>),
/// not the body — there is no impersonation surface here.
/// </summary>
public sealed class ResendInvoiceRequest
{
    [RouteParam]
    public required Guid InvoiceId { get; init; }
}
