using Avro;
using Basket.Sessions;
using EShop.BFF.IntegrationTests.Common;

namespace EShop.BFF.IntegrationTests.BasketPage;

/// <summary>
/// The live <c>bff-group</c> cache invalidator over Testcontainers Kafka + Schema Registry (issue #329
/// acceptance): a real <c>BasketCheckoutInitiatedEvent</c> produced to <c>basket.sessions</c> evicts the
/// seeded <c>basket-bff-{UserId}</c> entry — proving the consume → <c>RemoveByTag</c> path end-to-end for
/// the per-buyer basket tag (the dynamic-tag counterpart of the static home-page invalidation).
/// </summary>
[Collection<CacheInvalidationTestCollection>]
public sealed class BasketCacheInvalidationTests(CacheInvalidationTestFixture fixture)
{
    private static readonly TimeSpan EvictionTimeout = TimeSpan.FromSeconds(30);

    private readonly CacheInvalidationTestFixture _fixture = fixture;

    [Fact]
    public async Task BasketCheckoutInitiatedEvent_OnBasketSessions_EvictsTheBuyersBasket()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await _fixture.SeedBasketCacheAsync(userId);
        (await _fixture.IsBasketCachedAsync(userId)).Should().BeTrue("the basket was just seeded");

        // Act — the buyer checks out: the basket became an order.
        await _fixture.ProduceAsync(
            CacheInvalidationTestFixture.BasketSessionsTopic, userId, BuildCheckoutEvent(userId));

        // Assert — the live consumer removes the basket-bff-{userId} tag within the timeout.
        var evicted = await EventuallyEvictedAsync(userId);
        evicted.Should().BeTrue("the bff-group consumer should remove the buyer's basket tag on checkout");
    }

    private async Task<bool> EventuallyEvictedAsync(Guid userId)
    {
        var deadline = DateTime.UtcNow + EvictionTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!await _fixture.IsBasketCachedAsync(userId))
            {
                return true;
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        return !await _fixture.IsBasketCachedAsync(userId);
    }

    private static BasketCheckoutInitiatedEvent BuildCheckoutEvent(Guid userId)
    {
        var address = new CheckoutAddress
        {
            Street1 = "1 Main St",
            Street2 = null,
            City = "Town",
            State = null,
            PostalCode = "12345",
            CountryCode = "US",
        };

        return new BasketCheckoutInitiatedEvent
        {
            OrderId = Guid.NewGuid(),
            UserId = userId,
            Items = new List<BasketCheckoutItem>
            {
                new()
                {
                    ProductId = Guid.NewGuid(),
                    Sku = "SKU-1",
                    Name = "Product",
                    // Scale must match the schema's decimal(19,4) — a scale-0 literal fails to encode.
                    UnitPriceAmount = new AvroDecimal(10.0000m),
                    UnitPriceCurrency = "USD",
                    Quantity = 1,
                    LineTotal = new AvroDecimal(10.0000m),
                },
            },
            TotalAmount = new AvroDecimal(10.0000m),
            Currency = "USD",
            ShippingAddress = address,
            BillingAddress = address,
            PaymentMethodId = Guid.NewGuid(),
            InitiatedAtUtc = DateTime.UtcNow,
        };
    }
}
