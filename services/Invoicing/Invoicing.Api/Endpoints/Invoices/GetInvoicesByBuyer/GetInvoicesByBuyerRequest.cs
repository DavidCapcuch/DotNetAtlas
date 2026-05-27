namespace Invoicing.Api.Endpoints.Invoices.GetInvoicesByBuyer;

/// <summary>
/// HTTP request shape for <c>GET /api/v1/invoicing/invoices?pageNumber=&amp;pageSize=&amp;buyerId=</c>.
/// Buyer callers list only their own invoices. Admins may supply
/// <see cref="BuyerId"/> to scope the response to another buyer; non-admin callers
/// passing a <see cref="BuyerId"/> different from their own JWT subject are rejected
/// with 403. Omit <see cref="BuyerId"/> to fall back to caller-scope (the v1 default).
/// </summary>
public sealed class GetInvoicesByBuyerRequest
{
    /// <summary>
    /// Optional buyer scope override. Honoured only for admin callers; for non-admin
    /// callers it must equal the caller's own buyer id or the request is rejected.
    /// </summary>
    public Guid? BuyerId { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
