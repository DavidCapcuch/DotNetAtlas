using System.Net;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Endpoints.Orders.MarkOrderShipped;
using Ordering.Domain.Orders;
using Ordering.FunctionalTests.Common;
using Ordering.FunctionalTests.Common.TestClientInfrastructure;

namespace Ordering.FunctionalTests.ApiEndpoints.Orders;

[Collection(nameof(FunctionalTestCollection))]
public class MarkOrderShippedTests : BaseApiTest
{
    public MarkOrderShippedTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClientRegistry.NonAuthClient
            .POSTAsync<MarkOrderShippedEndpoint, MarkOrderShippedRequest>(
                new MarkOrderShippedRequest
                {
                    OrderId = Guid.CreateVersion7(),
                    Carrier = "DHL",
                    TrackingNumber = "TRK-1",
                });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenAuthenticatedAsBuyer_ReturnsForbidden()
    {
        var response = await HttpClientRegistry.BuyerClient
            .POSTAsync<MarkOrderShippedEndpoint, MarkOrderShippedRequest>(
                new MarkOrderShippedRequest
                {
                    OrderId = Guid.CreateVersion7(),
                    Carrier = "DHL",
                    TrackingNumber = "TRK-1",
                });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenOrderMissing_ReturnsNotFound()
    {
        var (response, _) = await HttpClientRegistry.AdminClient
            .POSTAsync<MarkOrderShippedEndpoint, MarkOrderShippedRequest, ProblemDetails>(
                new MarkOrderShippedRequest
                {
                    OrderId = Guid.CreateVersion7(),
                    Carrier = "DHL",
                    TrackingNumber = "TRK-1",
                });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenOrderConfirmed_ReturnsNoContentAndStatusShipped()
    {
        var seed = new OrderSeed(DbContext, App.FakeTime);
        var order = await seed.CreateConfirmedOrderAsync(TestUsers.BuyerId);

        var response = await HttpClientRegistry.AdminClient
            .POSTAsync<MarkOrderShippedEndpoint, MarkOrderShippedRequest>(
                new MarkOrderShippedRequest
                {
                    OrderId = order.Id,
                    Carrier = "DHL",
                    TrackingNumber = "TRK-42",
                });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var refreshed = await DbContext.Orders.AsNoTracking()
                .SingleAsync(o => o.Id == order.Id, TestContext.Current.CancellationToken);
            refreshed.Status.Should().Be(OrderStatus.Shipped);
            refreshed.Shipment!.Carrier.Should().Be("DHL");
            refreshed.Shipment.TrackingNumber.Should().Be("TRK-42");
        }
    }
}
