using System.Net;
using Basket.Api.Endpoints.Baskets.AddItem;
using Basket.Api.Endpoints.Baskets.Checkout;
using Basket.Api.Endpoints.Baskets.GetByUserId;
using Basket.Application.Baskets.GetByUserId;
using Basket.Domain.Baskets.ValueObjects;
using Basket.IntegrationTests.Common;
using FastEndpoints;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.Test.Framework.Assertions;

namespace Basket.IntegrationTests.Journeys;

/// <summary>
/// The basket-checkout journey (<c>basket.md</c> § 14.6): add → refresh → checkout. Pins that
/// checkout publishes the snapshot the refresh took — not the add-time price, and not a live Catalog
/// re-read (§ 12.2 snapshot-freeze) — and then ends the basket. Scope and cap:
/// eshop-master-design.md § 11.4.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class BasketCheckoutJourneyTests : BaseIntegrationTest
{
    public BasketCheckoutJourneyTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task AddRefreshCheckout_WhenCatalogPriceChangesBeforeCheckout_PublishesRefreshedPriceAndEndsBasket()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var client = HttpClientRegistry.RegularUserAuthClient(userId);

        // Add at the catalog's current price.
        StubCatalogPrice(productId, 10.00m);

        var addResponse = await client.POSTAsync<AddItemToBasketEndpoint, AddItemToBasketRequest>(
            new AddItemToBasketRequest { ProductId = productId, Quantity = 2 });

        addResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadTotalAsync(client)).Should().Be(20.00m);

        // The catalog price moves; refresh re-snapshots it into the basket.
        StubCatalogPrice(productId, 12.50m);

        var refreshResponse = await client.PostAsync("/api/v1/basket/refresh-prices", content: null, ct);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadTotalAsync(client)).Should().Be(25.00m);

        // The catalog moves again after the refresh, so a checkout that re-read it instead of
        // publishing the basket's snapshot would carry 99.00.
        StubCatalogPrice(productId, 99.00m);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.CreateVersion7().ToString());

        var (checkoutResponse, _) = await client
            .POSTAsync<CheckoutBasketEndpoint, CheckoutBasketRequest, CheckoutBasketResponse>(
                BasketTestData.ValidCheckoutRequest());

        checkoutResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // The committed row proves the publish survived the transaction; the payload rides on the
        // fake writer's capture because the row's Avro payload is empty.
        var committedRows = await DbContext.Set<OutboxMessage>()
            .Where(m => m.KafkaKey == userId.ToString())
            .ToListAsync(ct);
        var published = Fixture.GetFakeOutbox()
            .GetMessages<Basket.Sessions.BasketCheckoutInitiatedEvent>()
            .Where(m => m.KafkaKey == userId.ToString())
            .ToList();
        var (readResponse, basketAfter) = await client.GETAsync<GetBasketEndpoint, GetBasketResponse>();

        using (new AssertionScope())
        {
            committedRows.Should().ContainSingle()
                .Which.Type.Should().BeMessageType<Basket.Sessions.BasketCheckoutInitiatedEvent>();

            published.Should().ContainSingle()
                .Which.TopicName.Should().Be("basket.sessions");

            // Compared by value: the wire scale is BasketCheckoutInitiatedMapperTests' concern.
            var checkedOut = published[0].IntegrationEvent;
            checkedOut.Items.Should().ContainSingle();
            ((decimal)checkedOut.Items[0].UnitPriceAmount).Should().Be(12.50m);
            ((decimal)checkedOut.TotalAmount).Should().Be(25.00m);

            readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            basketAfter.Version.Should().Be(0, "checkout deletes the basket");
            basketAfter.Items.Should().BeEmpty();
        }
    }

    /// <summary>
    /// Points both Catalog ACL reads — the add-time lookup and the refresh batch — at one price, so
    /// the basket can only differ from the catalog by what it snapshotted, never by which read ran.
    /// </summary>
    private void StubCatalogPrice(Guid productId, decimal price)
    {
        var snapshot = BasketTestData.Snapshot(price);

        StubCatalogProduct(productId, snapshot);
        Catalog.GetManyAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok<IReadOnlyList<(Guid, ProductSnapshot)>>([(productId, snapshot)]));
    }

    private static async Task<decimal?> ReadTotalAsync(HttpClient client)
    {
        var (response, basket) = await client.GETAsync<GetBasketEndpoint, GetBasketResponse>();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return basket.Total?.Amount;
    }
}
