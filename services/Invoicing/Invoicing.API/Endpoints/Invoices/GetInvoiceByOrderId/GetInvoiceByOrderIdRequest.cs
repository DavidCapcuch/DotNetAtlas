using FastEndpoints;

namespace Invoicing.API.Endpoints.Invoices.GetInvoiceByOrderId;

/// <summary>
/// HTTP request shape for <c>GET /api/v1/invoicing/invoices/by-order/{orderId}</c>.
/// </summary>
public sealed class GetInvoiceByOrderIdRequest
{
    [RouteParam]
    public required Guid OrderId { get; init; }
}
