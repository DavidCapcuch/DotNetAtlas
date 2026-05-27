using Invoicing.Application.Invoices.GetInvoiceById;

namespace Invoicing.Application.Invoices.GetInvoicesByBuyer;

/// <summary>
/// Paged envelope for a buyer's invoices. Items reuse <see cref="GetInvoiceByIdResponse"/>
/// so summary and detail queries never drift.
/// </summary>
public sealed class GetInvoicesByBuyerResponse
{
    public required IReadOnlyList<GetInvoiceByIdResponse> Items { get; init; }

    public required int Total { get; init; }

    public required int PageNumber { get; init; }

    public required int PageSize { get; init; }
}
