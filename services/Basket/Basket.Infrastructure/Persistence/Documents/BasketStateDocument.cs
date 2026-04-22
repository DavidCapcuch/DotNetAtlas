using MemoryPack;

namespace Basket.Infrastructure.Persistence.Documents;

/// <summary>
/// Top-level Redis envelope for a persisted basket. Wraps the aggregate payload
/// in a version token so optimistic-concurrency checks can be performed by the
/// repository without introducing a second Redis key.
/// </summary>
/// <param name="Version">
/// Monotonic optimistic-concurrency token. Equals the aggregate's <c>Version</c>
/// at the moment it was saved.
/// </param>
/// <param name="Payload">The serialized basket aggregate state.</param>
[MemoryPackable]
public sealed partial record BasketStateDocument(
    int Version,
    BasketDocument Payload);
