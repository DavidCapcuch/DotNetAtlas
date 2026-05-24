using Platform.CQRS;

namespace Invoicing.Application.CreditNotes.GetCreditNoteById;

/// <summary>
/// Reads a single credit note by id. Authorization mirrors the invoice queries:
/// buyer sees only own; admin sees any. Cross-buyer lookups resolve to <c>NotFound</c>.
/// </summary>
public sealed record GetCreditNoteByIdQuery : IQuery<GetCreditNoteByIdResponse>
{
    public required Guid CreditNoteId { get; init; }

    public required Guid BuyerId { get; init; }

    public required bool IsAdmin { get; init; }
}
