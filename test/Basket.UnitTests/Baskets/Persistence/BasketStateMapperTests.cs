using Basket.Infrastructure.Persistence;
using Basket.Infrastructure.Persistence.Documents;
using Microsoft.Extensions.Time.Testing;
using Platform.SharedKernel.ValueObjects;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests.Baskets.Persistence;

/// <summary>
/// Bidirectional mapping tests for <see cref="BasketStateMapper"/>. The mapper
/// is the only translation between the domain aggregate and its MemoryPack
/// persistence mirror; round-trip parity is the invariant that keeps basket
/// state lossless across Redis saves.
/// </summary>
public class BasketStateMapperTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    [Fact]
    public void ToDocument_EmptyBasket_ProducesEnvelopeWithZeroVersionAndEmptyItems()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, UtcNow);
        _ = basket.PopDomainEvents();

        // Act
        var document = BasketStateMapper.ToDocument(basket);

        // Assert
        using (new AssertionScope())
        {
            document.Version.Should().Be(0);
            document.Payload.UserId.Should().Be(userId);
            document.Payload.Items.Should().BeEmpty();
            document.Payload.CreatedAtUtc.Should().Be(UtcNow);
            document.Payload.LastModifiedAtUtc.Should().Be(UtcNow);
        }
    }

    [Fact]
    public void ToDocument_PopulatedBasket_CopiesEveryFieldIncludingCurrencyAndPriceAmount()
    {
        // Arrange
        var basket = BasketAggregate.Create(Guid.CreateVersion7(), UtcNow);
        var productId = Guid.CreateVersion7();
        var capturedAt = new DateTimeOffset(2026, 01, 15, 09, 30, 00, TimeSpan.Zero);
        var snapshot = BasketTestData.Snapshot(
            amount: 42.50m,
            currency: CurrencyCode.Czk,
            sku: "SKU-XYZ",
            name: "Widget",
            capturedAtUtc: capturedAt);
        basket.AddItem(productId, snapshot, quantity: 4, UtcNow);
        _ = basket.PopDomainEvents();

        // Act
        var document = BasketStateMapper.ToDocument(basket);

        // Assert
        using (new AssertionScope())
        {
            document.Version.Should().Be(basket.Version).And.Be(1);
            document.Payload.Items.Should().ContainSingle();
            var line = document.Payload.Items[0];
            line.ProductId.Should().Be(productId);
            line.Quantity.Should().Be(4);
            line.Snapshot.Sku.Should().Be("SKU-XYZ");
            line.Snapshot.Name.Should().Be("Widget");
            line.Snapshot.PriceAmount.Should().Be(42.50m);
            line.Snapshot.PriceCurrencyName.Should().Be(CurrencyCode.Czk.Name);
            line.Snapshot.CapturedAtUtc.Should().Be(capturedAt);
        }
    }

    [Fact]
    public void ToDomain_EnvelopeWithItems_RehydratesAggregateWithoutRaisingCreationEvent()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var createdAt = new DateTimeOffset(2026, 01, 01, 08, 00, 00, TimeSpan.Zero);
        var lastModified = new DateTimeOffset(2026, 01, 10, 12, 30, 00, TimeSpan.Zero);
        var capturedAt = new DateTimeOffset(2026, 01, 05, 10, 15, 00, TimeSpan.Zero);

        var document = new BasketStateDocument(
            Version: 5,
            Payload: new BasketDocument(
                userId,
                new[]
                {
                    new BasketItemDocument(
                        productId,
                        new ProductSnapshotDocument("SKU-1", "Thing", 12.34m, CurrencyCode.Usd.Name, capturedAt),
                        Quantity: 2),
                },
                createdAt,
                lastModified));

        // Act
        var basket = BasketStateMapper.ToDomain(document);

        // Assert
        using (new AssertionScope())
        {
            basket.UserId.Should().Be(userId);
            basket.Version.Should().Be(5);
            basket.CreatedAtUtc.Should().Be(createdAt);
            basket.LastModifiedAtUtc.Should().Be(lastModified);
            basket.Items.Should().ContainSingle();
            var line = basket.Items.Single();
            line.ProductId.Should().Be(productId);
            line.Quantity.Should().Be(2);
            line.Snapshot.Price.Amount.Should().Be(12.34m);
            line.Snapshot.Price.Currency.Should().Be(CurrencyCode.Usd);
            line.Snapshot.Sku.Should().Be("SKU-1");
            line.Snapshot.Name.Should().Be("Thing");
            line.Snapshot.CapturedAtUtc.Should().Be(capturedAt);
            basket.PopDomainEvents().Should().BeEmpty(
                "Rehydrate must not emit BasketCreatedDomainEvent — that only fires on first creation");
        }
    }

    [Fact]
    public void RoundTrip_MutatedBasket_PreservesPublicStateExactly()
    {
        // Arrange
        var basket = BasketAggregate.Create(Guid.CreateVersion7(), UtcNow);
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(3));
        var productA = Guid.CreateVersion7();
        var productB = Guid.CreateVersion7();
        basket.AddItem(productA, BasketTestData.Snapshot(amount: 9.99m), quantity: 1, UtcNow);
        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(2));
        basket.AddItem(productB, BasketTestData.Snapshot(amount: 4.50m), quantity: 3, UtcNow);
        _ = basket.PopDomainEvents();

        // Act
        var document = BasketStateMapper.ToDocument(basket);
        var roundtripped = BasketStateMapper.ToDomain(document);

        // Assert
        using (new AssertionScope())
        {
            roundtripped.UserId.Should().Be(basket.UserId);
            roundtripped.Version.Should().Be(basket.Version);
            roundtripped.CreatedAtUtc.Should().Be(basket.CreatedAtUtc);
            roundtripped.LastModifiedAtUtc.Should().Be(basket.LastModifiedAtUtc);
            roundtripped.Items.Should().BeEquivalentTo(basket.Items);
            roundtripped.Total.Should().Be(basket.Total);
        }
    }
}
