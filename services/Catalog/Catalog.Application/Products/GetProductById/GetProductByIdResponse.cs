using Catalog.Application.Common.Contracts;

namespace Catalog.Application.Products.GetProductById;

/// <summary>
/// Published wire contract of the <c>GetProductById</c> endpoint, owned by this slice (ADR-0037).
/// Its shape coincides with the batch read's item type today; they are independent contracts free to
/// diverge, not duplication awaiting extraction.
/// </summary>
/// <remarks>
/// Two anti-corruption layers bind this payload by JSON property name with no compile-time link to
/// this assembly, so renaming any member is a breaking change the compiler cannot catch — a raw-JSON
/// characterization test over this endpoint in <c>Catalog.IntegrationTests</c> is what catches it:
/// <list type="bullet">
/// <item>Basket reads <see cref="ProductId"/>, <see cref="Sku"/>, <see cref="Name"/> and
/// <see cref="Price"/>, dropping the rest (basket.md § 9.3).</item>
/// <item>The BFF's <c>CatalogProductDto</c> reads all but <see cref="CategoryId"/> and the
/// timestamps (bff.md § 4.1) — and binds that same record to the <c>GetProductsByIds</c> batch
/// route as well, so it additionally assumes the two endpoints emit the same shape. That assumption
/// used to hold by construction and no longer does; it is now the BFF's to verify.</item>
/// </list>
/// The test pins accidental renames. A deliberate contract change updates it, so the consumer list
/// above is what tells you who to check before doing so.
/// </remarks>
public sealed record GetProductByIdResponse
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required Guid CategoryId { get; init; }

    public required string CategoryPath { get; init; }

    public required string CategoryBreadcrumb { get; init; }

    public required string BrandName { get; init; }

    /// <summary>
    /// Stays the BC-wide <see cref="MoneyDto"/>: value DTOs are shared where envelopes are not,
    /// because money representation has no endpoint-specific reason to change
    /// (ADR-0037 § Rationale — the volatility test). <see cref="Dimensions"/> and
    /// <see cref="Images"/> are shared on the same ruling.
    /// </summary>
    public required MoneyDto Price { get; init; }

    public required string Status { get; init; }

    /// <summary><c>null</c> when the product has no dimensions (digital/service products).</summary>
    public DimensionsDto? Dimensions { get; init; }

    public required IReadOnlyList<ImageReferenceDto> Images { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset LastUpdatedAtUtc { get; init; }
}
