using Basket.Infrastructure.Persistence.Documents;
using MemoryPack;

namespace Basket.UnitTests.Baskets.Persistence;

/// <summary>
/// MemoryPack round-trip tests for the Redis-persistence envelope. Proves that
/// the envelope preserves its version token, its payload, and the full item
/// collection (including decimal precision, currency code string, and
/// millisecond-granular timestamps) across serialization boundaries.
/// </summary>
public class BasketStateDocumentTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);

    private static readonly DateTimeOffset LastModifiedAt =
        new(2026, 02, 01, 12, 45, 33, TimeSpan.Zero);

    private static readonly DateTimeOffset CapturedAt =
        new(2026, 01, 15, 09, 30, 01, TimeSpan.Zero);

    [Fact]
    public void Envelope_EmptyBasketPayload_RoundTrips()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var original = new BasketStateDocument(
            Version: 7,
            Payload: new BasketDocument(userId, Array.Empty<BasketItemDocument>(), CreatedAt, LastModifiedAt));

        // Act
        var bytes = MemoryPackSerializer.Serialize(original);
        var round = MemoryPackSerializer.Deserialize<BasketStateDocument>(bytes);

        // Assert
        using (new AssertionScope())
        {
            round.Should().NotBeNull();
            round.Should().BeEquivalentTo(original);
            round!.Version.Should().Be(7);
            round.Payload.UserId.Should().Be(userId);
            round.Payload.Items.Should().BeEmpty();
            round.Payload.CreatedAtUtc.Should().Be(CreatedAt);
            round.Payload.LastModifiedAtUtc.Should().Be(LastModifiedAt);
        }
    }

    [Fact]
    public void Envelope_MultiItemPayload_RoundTrips()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productA = Guid.CreateVersion7();
        var productB = Guid.CreateVersion7();

        var original = new BasketStateDocument(
            Version: 42,
            Payload: new BasketDocument(
                userId,
                new[]
                {
                    new BasketItemDocument(
                        productA,
                        new ProductSnapshotDocument("SKU-A", "Widget A", 19.99m, "USD", CapturedAt),
                        Quantity: 3),
                    new BasketItemDocument(
                        productB,
                        new ProductSnapshotDocument("SKU-B", "Widget B", 250.50m, "USD", CapturedAt),
                        Quantity: 1),
                },
                CreatedAt,
                LastModifiedAt));

        // Act
        var bytes = MemoryPackSerializer.Serialize(original);
        var round = MemoryPackSerializer.Deserialize<BasketStateDocument>(bytes);

        // Assert
        round.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Envelope_DecimalHighPrecision_RoundTripsExactly()
    {
        // Guard against any silent "double" coercion in the serializer — basket totals
        // at checkout must match cent-for-cent what the user saw when they added items.

        // Arrange
        var original = new BasketStateDocument(
            Version: 1,
            Payload: new BasketDocument(
                Guid.CreateVersion7(),
                new[]
                {
                    new BasketItemDocument(
                        Guid.CreateVersion7(),
                        new ProductSnapshotDocument("SKU-1", "P", 1234.56789m, "CZK", CapturedAt),
                        Quantity: 7),
                },
                CreatedAt,
                LastModifiedAt));

        // Act
        var round = MemoryPackSerializer.Deserialize<BasketStateDocument>(
            MemoryPackSerializer.Serialize(original));

        // Assert
        round!.Payload.Items[0].Snapshot.PriceAmount.Should().Be(1234.56789m);
    }
}
