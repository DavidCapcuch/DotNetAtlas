using System.Net;
using FastEndpoints;
using Ordering.API.Endpoints.Orders.GetOrderById;
using Ordering.Application.Orders.GetOrderById;
using Ordering.FunctionalTests.Common;
using Ordering.FunctionalTests.Common.TestClientInfrastructure;

namespace Ordering.FunctionalTests.ApiEndpoints.Orders;

[Collection<FunctionalTestCollection>]
public class GetOrderByIdTests : BaseApiTest
{
    public GetOrderByIdTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClientRegistry.NonAuthClient
            .GETAsync<GetOrderByIdEndpoint, GetOrderByIdRequest, GetOrderByIdResponse>(
                new GetOrderByIdRequest { OrderId = Guid.CreateVersion7() });

        response.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenBuyerReadsOwnOrder_ReturnsOk()
    {
        var seed = new OrderSeed(DbContext, TimeProvider.System);
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrderByIdEndpoint, GetOrderByIdRequest, GetOrderByIdResponse>(
                new GetOrderByIdRequest { OrderId = order.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.OrderId.Should().Be(order.Id);
            payload.BuyerId.Should().Be(TestUsers.BuyerId);
            payload.Items.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task WhenAdminReadsAnotherBuyersOrder_ReturnsOk()
    {
        var seed = new OrderSeed(DbContext, TimeProvider.System);
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId);

        var (response, payload) = await HttpClientRegistry.AdminClient
            .GETAsync<GetOrderByIdEndpoint, GetOrderByIdRequest, GetOrderByIdResponse>(
                new GetOrderByIdRequest { OrderId = order.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.BuyerId.Should().Be(TestUsers.BuyerId);
        }
    }

    [Fact]
    public async Task WhenOtherBuyerReadsAnothersOrder_ReturnsNotFound()
    {
        var seed = new OrderSeed(DbContext, TimeProvider.System);
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId);

        var (response, _) = await HttpClientRegistry.OtherBuyerClient
            .GETAsync<GetOrderByIdEndpoint, GetOrderByIdRequest, ProblemDetails>(
                new GetOrderByIdRequest { OrderId = order.Id });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
