using Invoicing.Application.Invoices.GetInvoiceById;
using Platform.CQRS;

namespace Invoicing.Application.Invoices.GetInvoicesByBuyer;

/// <summary>
/// Lists a buyer's invoices, most-recent-first, paged with 1-indexed
/// <c>pageNumber</c> / <c>pageSize</c> (use-cases.md § Conventions). The endpoint
/// scopes buyer callers to their own JWT subject and lets an admin target another
/// buyer via the request's <c>BuyerId</c>; this query receives the already-resolved
/// <see cref="BuyerId"/>.
/// </summary>
public sealed record GetInvoicesByBuyerQuery : IQuery<GetInvoicesByBuyerResponse>
{
    public required Guid BuyerId { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
