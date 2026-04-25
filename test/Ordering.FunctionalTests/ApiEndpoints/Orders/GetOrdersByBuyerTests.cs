using System.Net;
using FastEndpoints;
using Ordering.API.Endpoints.Orders.GetOrdersByBuyer;
using Ordering.Application.Orders.GetOrdersByBuyer;
using Ordering.FunctionalTests.Common;
using Ordering.FunctionalTests.Common.TestClientInfrastructure;

namespace Ordering.FunctionalTests.ApiEndpoints.Orders;

[Collection(nameof(FunctionalTestCollection))]
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
    public async Task WhenBuyerHasOrders_ReturnsOnlyOwnOrders()
    {
        var seed = new OrderSeed(DbContext, App.FakeTime);
        var ownA = await seed.CreateOrderAsync(TestUsers.BuyerId);
        var ownB = await seed.CreateOrderAsync(TestUsers.BuyerId);
        var someoneElses = await seed.CreateOrderAsync(TestUsers.OtherBuyerId);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrdersByBuyerEndpoint, GetOrdersByBuyerRequest, GetOrdersByBuyerResponse>(
                new GetOrdersByBuyerRequest { Take = 10 });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Orders.Select(o => o.OrderId).Should()
                .BeEquivalentTo(new[] { ownA.Id, ownB.Id });
            payload.Orders.Should().NotContain(o => o.OrderId == someoneElses.Id);
        }
    }
}
