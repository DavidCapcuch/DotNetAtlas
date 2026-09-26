using System.Net;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Ordering.Api.Endpoints.Orders.MarkOrderDelivered;
using Ordering.Domain.Orders;
using Ordering.IntegrationTests.Common;
using Ordering.IntegrationTests.Common.TestClientInfrastructure;

namespace Ordering.IntegrationTests.ApiEndpoints.Orders;

[Collection<IntegrationTestCollection>]
public class MarkOrderDeliveredTests : BaseIntegrationTest
{
    public MarkOrderDeliveredTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "security")]
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
    [Trait("Category", "critical-path")]
    public async Task WhenOrderShipped_ReturnsNoContentAndStatusDelivered()
    {
        var seed = new OrderSeed(DbContext, TimeProvider.System);
        var order = await seed.CreateShippedOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);

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
