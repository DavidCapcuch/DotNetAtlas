using System.Net;
using FastEndpoints;
using Microsoft.Extensions.Time.Testing;
using Ordering.Api.Endpoints.Orders.GetOrderById;
using Ordering.Application.Orders.GetOrderById;
using Ordering.Domain.Orders;
using Ordering.IntegrationTests.Common;
using Ordering.IntegrationTests.Common.TestClientInfrastructure;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.IntegrationTests.ApiEndpoints.Orders;

/// <summary>
/// Slice tests for the read-by-id endpoint. Beyond auth, these pin the SQL-side projection of the
/// three mutually-exclusive optional VOs (<c>Cancellation</c> / <c>Failure</c> / <c>Shipment</c>) —
/// EF Core's InMemory provider cannot translate that conditional projection, so the real Postgres
/// container is what makes the assertions meaningful (ADR-0021).
/// </summary>
[Collection<IntegrationTestCollection>]
public class GetOrderByIdTests : BaseIntegrationTest
{
    // ADR-0015: lifecycle timestamps are seeded a whole minute apart on a pinned clock, so a
    // projection that reads the wrong column lands on a different minute and fails exact equality.
    private static readonly DateTimeOffset PinnedNow =
        new(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

    public GetOrderByIdTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClientRegistry.NonAuthClient
            .GETAsync<GetOrderByIdEndpoint, GetOrderByIdRequest, GetOrderByIdResponse>(
                new GetOrderByIdRequest { OrderId = Guid.CreateVersion7() });

        response.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task WhenBuyerReadsOwnOrder_ReturnsOk()
    {
        var seed = new OrderSeed(DbContext, TimeProvider.System);
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrderByIdEndpoint, GetOrderByIdRequest, GetOrderByIdResponse>(
                new GetOrderByIdRequest { OrderId = order.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.OrderId.Should().Be(order.Id);
            payload.BuyerId.Should().Be(TestUsers.BuyerId);
            payload.PaymentMethodId.Should().Be(order.PaymentMethodId);
            payload.Status.Should().Be(OrderStatus.Created.Name);

            // Owned Money + the owned OrderItem collection survive the projection.
            payload.TotalAmount.Should().Be(19.98m);
            payload.Currency.Should().Be(CurrencyCode.Eur.Name);
            payload.Items.Should().ContainSingle();

            // Shipping and billing are seeded with different values, so a projection that fills
            // one address from the other's columns fails here.
            payload.ShippingAddress.City.Should().Be("Prague");
            payload.ShippingAddress.PostalCode.Should().Be("11000");
            payload.BillingAddress.City.Should().Be("Brno");
            payload.BillingAddress.PostalCode.Should().Be("60200");

            // An active order carries none of the three optional VOs.
            payload.Cancellation.Should().BeNull();
            payload.Failure.Should().BeNull();
            payload.Shipment.Should().BeNull();
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenAdminReadsAnotherBuyersOrder_ReturnsOk()
    {
        var seed = new OrderSeed(DbContext, TimeProvider.System);
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);

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
    [Trait("Category", "security")]
    public async Task WhenOtherBuyerReadsAnothersOrder_ReturnsNotFound()
    {
        var seed = new OrderSeed(DbContext, TimeProvider.System);
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);

        var (response, problem) = await HttpClientRegistry.OtherBuyerClient
            .GETAsync<GetOrderByIdEndpoint, GetOrderByIdRequest, ProblemDetails>(
                new GetOrderByIdRequest { OrderId = order.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // A cross-buyer read is answered with the SAME error a missing order produces, so the
            // response body cannot be used to distinguish "exists but not yours" from "absent".
            problem.Errors.Should().ContainSingle();
            var error = problem.Errors.Single();
            error.Code.Should().Be("Order.NotFound");
            error.Reason.Should().Contain(order.Id.ToString());
        }
    }

    [Fact]
    public async Task WhenOrderMissing_ReturnsNotFound()
    {
        var missing = Guid.CreateVersion7();

        var (response, problem) = await HttpClientRegistry.AdminClient
            .GETAsync<GetOrderByIdEndpoint, GetOrderByIdRequest, ProblemDetails>(
                new GetOrderByIdRequest { OrderId = missing });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);

            problem.Errors.Should().ContainSingle();
            var error = problem.Errors.Single();
            error.Code.Should().Be("Order.NotFound");
            error.Reason.Should().Contain(missing.ToString());
        }
    }

    [Fact]
    public async Task WhenOrderCancelled_ReturnsOnlyCancellationPopulated()
    {
        const string reason = "Buyer requested cancellation";
        var cancelledAtUtc = PinnedNow.AddMinutes(1);

        var seed = new OrderSeed(DbContext, new FakeTimeProvider(PinnedNow));
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);
        order.Cancel(reason, cancelledAtUtc).IsSuccess.Should().BeTrue();
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrderByIdEndpoint, GetOrderByIdRequest, GetOrderByIdResponse>(
                new GetOrderByIdRequest { OrderId = order.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Status.Should().Be(OrderStatus.Cancelled.Name);
            payload.Cancellation.Should().NotBeNull();
            payload.Cancellation!.Reason.Should().Be(reason);
            payload.Cancellation.AtStatus.Should().Be(OrderStatus.Created.Name);
            payload.Cancellation.CancelledAtUtc.Should().Be(cancelledAtUtc);
            payload.Failure.Should().BeNull();
            payload.Shipment.Should().BeNull();
        }
    }

    [Fact]
    public async Task WhenOrderFailed_ReturnsOnlyFailurePopulated()
    {
        const string errorCode = "STOCK_UNAVAILABLE";
        const string errorMessage = "Insufficient stock for requested items";
        var failedAtUtc = PinnedNow.AddMinutes(1);

        // Failed has no HTTP transition of its own (the saga drives it over Kafka), so the FSM is
        // walked on the aggregate while arranging — the endpoint is still the entrance under test.
        var seed = new OrderSeed(DbContext, new FakeTimeProvider(PinnedNow));
        var order = await seed.CreateOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);
        order.Fail(errorCode, errorMessage, failedAtUtc).IsSuccess.Should().BeTrue();
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrderByIdEndpoint, GetOrderByIdRequest, GetOrderByIdResponse>(
                new GetOrderByIdRequest { OrderId = order.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Status.Should().Be(OrderStatus.Failed.Name);
            payload.Failure.Should().NotBeNull();
            payload.Failure!.ErrorCode.Should().Be(errorCode);
            payload.Failure.ErrorMessage.Should().Be(errorMessage);
            payload.Failure.AtStatus.Should().Be(OrderStatus.Created.Name);
            payload.Failure.FailedAtUtc.Should().Be(failedAtUtc);
            payload.Cancellation.Should().BeNull();
            payload.Shipment.Should().BeNull();
        }
    }

    [Fact]
    public async Task WhenOrderShipped_ReturnsOnlyShipmentPopulated()
    {
        // Every clock read advances a minute, so each seeded transition gets its own timestamp.
        var clock = new FakeTimeProvider(PinnedNow) { AutoAdvanceAmount = TimeSpan.FromMinutes(1) };
        var seed = new OrderSeed(DbContext, clock);
        var order = await seed.CreateShippedOrderAsync(TestUsers.BuyerId, cancellationToken: TestContext.Current.CancellationToken);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetOrderByIdEndpoint, GetOrderByIdRequest, GetOrderByIdResponse>(
                new GetOrderByIdRequest { OrderId = order.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Status.Should().Be(OrderStatus.Shipped.Name);
            payload.Shipment.Should().NotBeNull();
            payload.Shipment!.Carrier.Should().Be("DHL");
            payload.Shipment.TrackingNumber.Should().Be("1Z999AA10123456784");
            payload.Shipment.ShippedAtUtc.Should().Be(order.Shipment!.ShippedAtUtc);
            payload.Shipment.ShippedAtUtc.Should().NotBe(order.ConfirmedAtUtc!.Value);
            payload.Cancellation.Should().BeNull();
            payload.Failure.Should().BeNull();
        }
    }
}
