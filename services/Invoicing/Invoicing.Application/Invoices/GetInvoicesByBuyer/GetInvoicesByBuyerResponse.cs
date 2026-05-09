using Invoicing.Application.Invoices.GetInvoiceById;

namespace Invoicing.Application.Invoices.GetInvoicesByBuyer;

/// <summary>
/// Paged envelope for a buyer's invoices. Items reuse <see cref="GetInvoiceByIdResponse"/>
/// so summary and detail queries never drift.
/// </summary>
public sealed class GetInvoicesByBuyerResponse
{
    public required IReadOnlyList<GetInvoiceByIdResponse> Invoices { get; init; }

    public required int Skip { get; init; }

    public required int Take { get; init; }
}
