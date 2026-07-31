using System.ComponentModel;

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
    /// <summary>Page served when the caller omits <c>pageNumber</c>.</summary>
    public const int DefaultPageNumber = 1;

    /// <summary>Page size served when the caller omits <c>pageSize</c>.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Optional buyer scope override. Honoured only for admin callers; for non-admin
    /// callers it must equal the caller's own buyer id or the request is rejected.
    /// </summary>
    public Guid? BuyerId { get; init; }

    /// <summary>
    /// 1-indexed page. Nullable because it is genuinely optional: the generated OpenAPI
    /// document derives parameter requiredness from nullability, so a non-nullable member
    /// would publish this as mandatory while the endpoint supplies
    /// <see cref="DefaultPageNumber"/> for callers who omit it.
    /// </summary>
    [DefaultValue(DefaultPageNumber)]
    public int? PageNumber { get; init; }

    /// <summary>Nullable for the same reason as <see cref="PageNumber"/>.</summary>
    [DefaultValue(DefaultPageSize)]
    public int? PageSize { get; init; }
}
