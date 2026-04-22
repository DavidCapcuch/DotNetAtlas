using MemoryPack;

namespace Basket.Infrastructure.Persistence.Documents;

/// <summary>
/// Persistence mirror of the <c>Basket</c> aggregate root. Kept separate from the
/// domain class so the domain stays free of serialization attributes and the
/// <c>Money</c> value object (which lives in <c>Platform.SharedKernel</c>) does
/// not need to be annotated <c>[MemoryPackable]</c>. The repository maps
/// Domain &#x2194; Document at the persistence seam.
/// </summary>
/// <param name="UserId">The basket owner's identifier (also the aggregate Id).</param>
/// <param name="Items">All line items at the time of serialization.</param>
/// <param name="CreatedAtUtc">Instant the basket was first created.</param>
/// <param name="LastModifiedAtUtc">Instant of the most recent mutation.</param>
[MemoryPackable]
public sealed partial record BasketDocument(
    Guid UserId,
    IReadOnlyList<BasketItemDocument> Items,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastModifiedAtUtc);
