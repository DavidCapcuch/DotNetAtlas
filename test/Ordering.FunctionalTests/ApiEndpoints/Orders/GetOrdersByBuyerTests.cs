using System.Net;
using FastEndpoints;
using Ordering.API.Endpoints.Orders.GetOrdersByBuyer;
using Ordering.Application.Orders.GetOrdersByBuyer;
using Ordering.FunctionalTests.Common;
using Ordering.FunctionalTests.Common.TestClientInfrastructure;

namespace Ordering.FunctionalTests.ApiEndpoints.Orders;

[Collection<FunctionalTestCollection>]
public class GetOrdersByBuyerTests : BaseApiTest
{
    public GetOrdersByBuyerTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClientRegistry.NonAuthClient
            .GETAsync<GetOrdersByBuyerEndpoint, GetOrdersByBuyerRequest, GetOrdersByBuyerResponse>(
                new GetOrdersByBuyerRequest());

        response.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenBuyerHasOrders_ReturnsOnlyOwnOrdersAndPagingEnvelope()
    {
        var seed = new OrderSeed(DbContext, App.FakeTime);
        var ownA = await seed.CreateOrderAsync(TestUsers.BuyerId);
        var ownB = await seed.CreateOrderAsync(TestUsers.BuyerId);
        var someoneElses = await seed.CreateOrderAsync(TestUsers.OtherBuyerId);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrdersByBuyerEndpoint, GetOrdersByBuyerRequest, GetOrdersByBuyerResponse>(
                new GetOrdersByBuyerRequest { PageNumber = 1, PageSize = 10 });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Total.Should().Be(2);
            payload.PageNumber.Should().Be(1);
            payload.PageSize.Should().Be(10);
            payload.Items.Select(o => o.OrderId).Should()
                .BeEquivalentTo(new[] { ownA.Id, ownB.Id });
            payload.Items.Should().NotContain(o => o.OrderId == someoneElses.Id);
            payload.Items.Should().AllSatisfy(item =>
            {
                item.ItemCount.Should().Be(1);
                item.LastStatusChangeAtUtc.Should().Be(item.CreatedAtUtc);
            });
        }
    }
}
