using Avro;
using Avro.Specific;
using Basket.Sessions;
using Catalog.Categories;
using Catalog.Products;
using EShop.BFF.Infrastructure.Messaging;
using Inventory.Stock;

namespace EShop.BFF.UnitTests.Messaging;

/// <summary>
/// The <c>bff-group</c> invalidation contract (bff.md § 2.2 / § 3.4): every Catalog product/category
/// lifecycle event and every Inventory stock-level change removes the <c>home-page</c> tag; an event the
/// BFF does not map invalidates nothing. (Avro records are constructed inside each fact rather than passed
/// as theory data — xUnit cannot serialize an <see cref="ISpecificRecord"/> for a display name.)
/// </summary>
public sealed class CacheInvalidationTagMapTests
{
    private const string HomePageTag = "home-page";

    [Fact]
    public void TagsFor_ProductCreatedEvent_RemovesHomePageTag() =>
        AssertInvalidatesHomePage(new ProductCreatedEvent());

    [Fact]
    public void TagsFor_ProductPriceChangedEvent_RemovesHomePageTag() =>
        AssertInvalidatesHomePage(new ProductPriceChangedEvent());

    [Fact]
    public void TagsFor_ProductDiscontinuedEvent_RemovesHomePageTag() =>
        AssertInvalidatesHomePage(new ProductDiscontinuedEvent());

    [Fact]
    public void TagsFor_CategoryCreatedEvent_RemovesHomePageTag() =>
        AssertInvalidatesHomePage(new CategoryCreatedEvent());

    [Fact]
    public void TagsFor_StockLevelChangedEvent_RemovesHomePageTag() =>
        AssertInvalidatesHomePage(new StockLevelChangedEvent());

    [Fact]
    public void TagsFor_BasketCheckoutInitiatedEvent_RemovesTheBuyersBasketTag()
    {
        var userId = Guid.NewGuid();

        var tags = CacheInvalidationTagMap.TagsFor(new BasketCheckoutInitiatedEvent { UserId = userId });

        tags.Should().ContainSingle().Which.Should().Be($"basket-bff-{userId}");
    }

    [Fact]
    public void TagsFor_UnmappedEvent_RemovesNothing()
    {
        var tags = CacheInvalidationTagMap.TagsFor(new UnmappedEvent());

        tags.Should().BeEmpty();
    }

    private static void AssertInvalidatesHomePage(ISpecificRecord @event)
    {
        var tags = CacheInvalidationTagMap.TagsFor(@event);

        tags.Should().ContainSingle().Which.Should().Be(HomePageTag);
    }

    private sealed class UnmappedEvent : ISpecificRecord
    {
        public Schema Schema => throw new NotSupportedException();

        public object Get(int fieldPos) => throw new NotSupportedException();

        public void Put(int fieldPos, object fieldValue) => throw new NotSupportedException();
    }
}
