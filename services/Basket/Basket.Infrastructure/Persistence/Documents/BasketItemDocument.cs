using MemoryPack;

namespace Basket.Infrastructure.Persistence.Documents;

/// <summary>
/// Persistence mirror of <c>BasketItem</c>. One line of the serialized basket.
/// </summary>
/// <param name="ProductId">Catalog product identifier (reference by id only — Vernon rule 3).</param>
/// <param name="Snapshot">Frozen product data captured at add-time.</param>
/// <param name="Quantity">Number of units (&#x2265; 1, enforced by the domain).</param>
[MemoryPackable]
public sealed partial record BasketItemDocument(
    Guid ProductId,
    ProductSnapshotDocument Snapshot,
    int Quantity);
