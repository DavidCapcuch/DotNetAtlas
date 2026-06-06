using MemoryPack;

namespace Inventory.Infrastructure.Persistence.Caching;

/// <summary>
/// MemoryPack wire shape for a cached <c>current_stock_levels</c> row on <c>redis-cache</c>.
/// A dedicated, serializer-annotated record keeps the MemoryPack attribute out of the
/// Application-layer <c>StockLevelResponse</c> DTO; <see cref="FusionStockLevelCache"/>
/// maps between the two. Mirrors the Basket <c>*Document</c> MemoryPack convention.
/// </summary>
[MemoryPackable]
public sealed partial record CachedStockLevel(
    Guid ProductId,
    int OnHand,
    int Reserved,
    int Available,
    DateTimeOffset LastUpdatedUtc,
    int LastVersion);
