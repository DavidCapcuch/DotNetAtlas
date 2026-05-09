using FastEndpoints;

namespace Invoicing.API.Endpoints.CreditNotes.GetCreditNoteById;

/// <summary>
/// HTTP request shape for <c>GET /api/v1/invoicing/credit-notes/{creditNoteId}</c>.
/// </summary>
public sealed class GetCreditNoteByIdRequest
{
    [RouteParam]
    public required Guid CreditNoteId { get; init; }
}
