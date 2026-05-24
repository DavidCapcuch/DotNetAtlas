using Invoicing.Application.Invoices.GetInvoiceById;
using Platform.CQRS;

namespace Invoicing.Application.Invoices.GetInvoicesByBuyer;

/// <summary>
/// Lists the calling buyer's invoices, most-recent-first, paged with offset / limit
/// (use-cases.md § Conventions). Admin override (an admin requesting another buyer's
/// invoices) is deferred to v2+; v1 always scopes to the JWT subject.
/// </summary>
public sealed record GetInvoicesByBuyerQuery : IQuery<GetInvoicesByBuyerResponse>
{
    public required Guid BuyerId { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = 20;
}
