namespace Invoicing.API.Endpoints.Invoices.GetInvoicesByBuyer;

/// <summary>
/// HTTP request shape for <c>GET /api/v1/invoicing/invoices?skip=&amp;take=</c>. v1 lists
/// only the caller's own invoices — admin override (<c>?buyerId=</c>) is deferred to v2+.
/// </summary>
public sealed class GetInvoicesByBuyerRequest
{
    public int Skip { get; init; }

    public int Take { get; init; } = 20;
}
