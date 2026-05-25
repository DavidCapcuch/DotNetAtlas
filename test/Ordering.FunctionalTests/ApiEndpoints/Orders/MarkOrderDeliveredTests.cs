using System.Net;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Endpoints.Orders.MarkOrderDelivered;
using Ordering.Domain.Orders;
using Ordering.FunctionalTests.Common;
using Ordering.FunctionalTests.Common.TestClientInfrastructure;

namespace Ordering.FunctionalTests.ApiEndpoints.Orders;

[Collection<FunctionalTestCollection>]
public class MarkOrderDeliveredTests : BaseApiTest
{
    public MarkOrderDeliveredTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenAuthenticatedAsBuyer_ReturnsForbidden()
    {
        var response = await HttpClientRegistry.BuyerClient
            .POSTAsync<MarkOrderDeliveredEndpoint, MarkOrderDeliveredRequest>(
                new MarkOrderDeliveredRequest { OrderId = Guid.CreateVersion7() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenOrderMissing_ReturnsNotFound()
    {
        var (response, _) = await HttpClientRegistry.AdminClient
            .POSTAsync<MarkOrderDeliveredEndpoint, MarkOrderDeliveredRequest, ProblemDetails>(
                new MarkOrderDeliveredRequest { OrderId = Guid.CreateVersion7() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenOrderShipped_ReturnsNoContentAndStatusDelivered()
    {
        var seed = new OrderSeed(DbContext, App.FakeTime);
        var order = await seed.CreateShippedOrderAsync(TestUsers.BuyerId);

        var response = await HttpClientRegistry.AdminClient
            .POSTAsync<MarkOrderDeliveredEndpoint, MarkOrderDeliveredRequest>(
                new MarkOrderDeliveredRequest { OrderId = order.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var refreshed = await DbContext.Orders.AsNoTracking()
                .SingleAsync(o => o.Id == order.Id, TestContext.Current.CancellationToken);
            refreshed.Status.Should().Be(OrderStatus.Delivered);
            refreshed.DeliveredAtUtc.Should().NotBeNull();
        }
    }
}
