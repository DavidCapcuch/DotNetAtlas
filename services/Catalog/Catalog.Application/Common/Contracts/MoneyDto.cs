namespace Catalog.Application.Common.Contracts;

/// <summary>
/// Money on the wire (amount + ISO-4217 currency), shared by product slices that accept or
/// return a price. Lives in <c>Common.Contracts</c> so no feature slice owns a type its siblings
/// depend on.
/// </summary>
public sealed record MoneyDto
{
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }
}
