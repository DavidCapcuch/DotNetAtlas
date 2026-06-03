using System.Net;
using FastEndpoints;
using Payments.Api.Endpoints.Payments.GetPaymentsByOrder;
using Payments.Application.Transactions.GetPaymentsByOrder;
using Payments.FunctionalTests.Common;
using Payments.FunctionalTests.Common.TestClientInfrastructure;

namespace Payments.FunctionalTests.ApiEndpoints.Payments;

[Collection<FunctionalTestCollection>]
public class GetPaymentsByOrderTests : BaseApiTest
{
    public GetPaymentsByOrderTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClientRegistry.NonAuthClient
            .GETAsync<GetPaymentsByOrderEndpoint, GetPaymentsByOrderRequest, GetPaymentsByOrderResponse>(
                new GetPaymentsByOrderRequest { OrderId = Guid.CreateVersion7() });

        response.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenAuthenticatedWithoutAdminRole_ReturnsForbidden()
    {
        var response = await HttpClientRegistry.UserClient
            .GETAsync<GetPaymentsByOrderEndpoint, GetPaymentsByOrderRequest, GetPaymentsByOrderResponse>(
                new GetPaymentsByOrderRequest { OrderId = Guid.CreateVersion7() });

        response.Response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenAdminAndNoPaymentsForOrder_ReturnsOkWithEmptyList()
    {
        // Handler intentionally returns an empty list rather than 404 — orders
        // can exist before any payment is requested.
        var orderId = Guid.CreateVersion7();

        var (response, payload) = await HttpClientRegistry.AdminClient
            .GETAsync<GetPaymentsByOrderEndpoint, GetPaymentsByOrderRequest, GetPaymentsByOrderResponse>(
                new GetPaymentsByOrderRequest { OrderId = orderId });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.OrderId.Should().Be(orderId);
            payload.Payments.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task WhenAdminAndOnePaymentForOrder_ReturnsOkWithOnePayment()
    {
        var orderId = Guid.CreateVersion7();
        var seeded = await PaymentSeed.InsertRequestedAsync(DbContext, orderId: orderId);

        var (response, payload) = await HttpClientRegistry.AdminClient
            .GETAsync<GetPaymentsByOrderEndpoint, GetPaymentsByOrderRequest, GetPaymentsByOrderResponse>(
                new GetPaymentsByOrderRequest { OrderId = orderId });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.OrderId.Should().Be(orderId);
            payload.Payments.Should().ContainSingle()
                .Which.PaymentId.Should().Be(seeded.Id);
        }
    }

    // ADR-0029 establishes one payment per order (unique ux_payment_transactions_order_id), so a
    // "multiple payments for one order" scenario is no longer representable — the admin projection
    // returns at most the single payment (covered by WhenAdminAndOnePaymentForOrder above).
}
