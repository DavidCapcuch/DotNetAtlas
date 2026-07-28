namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Money on the wire (amount + ISO-4217 currency), shared by the product slices that accept or
/// return a price. Share/duplicate ruling: ADR-0037 § Implementation Notes.
/// </summary>
public sealed record MoneyDto
{
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
}
