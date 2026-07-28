namespace Catalog.Application.Common.ReadModels;

/// <summary>
/// The persisted shape of one element of the <c>images_json</c> JSONB column on
/// <c>product_search_view</c>.
/// </summary>
/// <remarks>
/// Deliberately separate from the <c>ImageReferenceDto</c> wire contract it is mapped to and from,
/// even though the two carry the same members today. They have different reasons to change: a
/// member added for one endpoint's screen must not alter bytes already at rest, and a key stored
/// last year must keep deserializing after the API renames its own. Sharing one type would make
/// every contract edit a silent rewrite of the stored shape's meaning — see
/// <see cref="ProductSearchViewMapper"/> and ADR-0021.
/// <para>
/// Its member names ARE the stored column keys. Changing one is a persistence migration, not a
/// rename; the frozen-literal test in <c>ProductSearchViewMapperTests</c> is what says so.
/// </para>
/// </remarks>
internal sealed record ProductImageDocument
{
    public required string Url { get; init; }

    public required string AltText { get; init; }

    public required int DisplayOrder { get; init; }
}
