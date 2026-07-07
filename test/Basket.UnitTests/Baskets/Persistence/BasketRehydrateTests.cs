using Basket.Domain.Baskets.Events;
using Basket.Domain.Baskets.ValueObjects;
using Microsoft.Extensions.Time.Testing;
using Platform.SharedKernel.Exceptions;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests.Baskets.Persistence;

/// <summary>
/// Covers the <c>Basket.Rehydrate</c> internal factory used exclusively by the
/// persistence seam. The factory must restore state exactly and must NOT raise
/// any domain events — rehydrating a basket from Redis is not a domain-level
/// "creation" or "mutation".
/// </summary>
public class BasketRehydrateTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    private DateTimeOffset UtcNow => _fakeTimeProvider.GetUtcNow();

    [Fact]
    public void Rehydrate_RestoresVersionAndTimestampsExactly_AndDoesNotRaiseEvents()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var createdAt = new DateTimeOffset(2026, 01, 01, 08, 00, 00, TimeSpan.Zero);
        var lastModified = new DateTimeOffset(2026, 01, 10, 12, 30, 00, TimeSpan.Zero);
        var items = new List<BasketItem>();

        // Act
        var basket = BasketAggregate.Rehydrate(userId, version: 7, createdAt, lastModified, items);

        // Assert
        using (new AssertionScope())
        {
            basket.UserId.Should().Be(userId);
            basket.Version.Should().Be(7);
            basket.CreatedAtUtc.Should().Be(createdAt);
            basket.LastModifiedAtUtc.Should().Be(lastModified);
            basket.Items.Should().BeEmpty();
            basket.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Rehydrate_WithItems_ExposesThemAsReadOnlyAndPreservesOrder()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var productA = Guid.CreateVersion7();
        var productB = Guid.CreateVersion7();
        var items = new List<BasketItem>
        {
            BasketItem.BuildUnchecked(productA, BasketTestData.Snapshot(amount: 1m), quantity: 1),
            BasketItem.BuildUnchecked(productB, BasketTestData.Snapshot(amount: 2m), quantity: 4),
        };

        // Act
        var basket = BasketAggregate.Rehydrate(userId, version: 3, UtcNow, UtcNow, items);

        // Assert
        using (new AssertionScope())
        {
            basket.Items.Should().HaveCount(2);
            basket.Items.ElementAt(0).ProductId.Should().Be(productA);
            basket.Items.ElementAt(1).ProductId.Should().Be(productB);
            basket.Items.ElementAt(1).Quantity.Should().Be(4);
        }
    }

    [Fact]
    public void Rehydrate_WhenEmptyUserId_ThrowsDataIntegrityException()
    {
        // Act
        var act = () => BasketAggregate.Rehydrate(
            Guid.Empty, version: 0, UtcNow, UtcNow, Array.Empty<BasketItem>());

        // Assert
        act.Should().Throw<DataIntegrityException>().WithMessage("*UserId*");
    }

    [Fact]
    public void Rehydrated_BasketAcceptsMutations_AndContinuesVersioningFromRestoredVersion()
    {
        // A rehydrated basket must behave identically to a freshly-created one for mutations —
        // the point of the persistence seam is invisibility to the domain.

        // Arrange
        var basket = BasketAggregate.Rehydrate(
            Guid.CreateVersion7(),
            version: 10,
            UtcNow,
            UtcNow,
            Array.Empty<BasketItem>());

        _fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
        var utcAtAdd = UtcNow;

        // Act
        var result = basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), quantity: 1, utcAtAdd);

        // Assert
        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            basket.Version.Should().Be(11);
            basket.LastModifiedAtUtc.Should().Be(utcAtAdd);
            basket.PopDomainEvents().Should().ContainSingle()
                .Which.Should().BeOfType<ItemAddedToBasketDomainEvent>();
        }
    }

    [Fact]
    public void Rehydrate_WhenItemsIsNull_Throws()
    {
        // Act
        var act = () => BasketAggregate.Rehydrate(
            Guid.CreateVersion7(), version: 0, UtcNow, UtcNow, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
