using FastEndpoints;

namespace Invoicing.API.Endpoints.Invoices.GetInvoiceById;

/// <summary>
/// HTTP request shape for <c>GET /api/v1/invoicing/invoices/{invoiceId}</c>.
/// </summary>
public sealed class GetInvoiceByIdRequest
{
    [RouteParam]
    public required Guid InvoiceId { get; init; }
}
