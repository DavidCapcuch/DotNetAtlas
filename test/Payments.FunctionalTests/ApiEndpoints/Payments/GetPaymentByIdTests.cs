using System.Net;
using FastEndpoints;
using Payments.Api.Endpoints.Payments.GetPaymentById;
using Payments.Application.Transactions.GetPaymentById;
using Payments.FunctionalTests.Common;
using Payments.FunctionalTests.Common.TestClientInfrastructure;

namespace Payments.FunctionalTests.ApiEndpoints.Payments;

[Collection(nameof(FunctionalTestCollection))]
public class GetPaymentByIdTests : BaseApiTest
{
    public GetPaymentByIdTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClientRegistry.NonAuthClient
            .GETAsync<GetPaymentByIdEndpoint, GetPaymentByIdRequest, GetPaymentByIdResponse>(
                new GetPaymentByIdRequest { PaymentId = Guid.CreateVersion7() });

        response.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenAuthenticatedWithoutAdminRole_ReturnsForbidden()
    {
        var response = await HttpClientRegistry.UserClient
            .GETAsync<GetPaymentByIdEndpoint, GetPaymentByIdRequest, GetPaymentByIdResponse>(
                new GetPaymentByIdRequest { PaymentId = Guid.CreateVersion7() });

        response.Response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenAdminRoleButMissingPaymentsReadScope_ReturnsForbidden()
    {
        // Proves the PaymentsAdmin policy enforces BOTH the realm role AND the
        // `payments.read` scope claim per ADR-0010.
        var response = await HttpClientRegistry.AdminWithoutScopeClient
            .GETAsync<GetPaymentByIdEndpoint, GetPaymentByIdRequest, GetPaymentByIdResponse>(
                new GetPaymentByIdRequest { PaymentId = Guid.CreateVersion7() });

        response.Response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenAdminAndPaymentDoesNotExist_ReturnsNotFound()
    {
        var (response, problem) = await HttpClientRegistry.AdminClient
            .GETAsync<GetPaymentByIdEndpoint, GetPaymentByIdRequest, ProblemDetails>(
                new GetPaymentByIdRequest { PaymentId = Guid.CreateVersion7() });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            problem.Errors.Should()
                .Contain(e =>
                    string.Equals(e.Name, "PaymentId", StringComparison.OrdinalIgnoreCase)
                    && e.Reason.Contains("does not exist", StringComparison.Ordinal)
                    && e.Code == "Payments.NotFound");
        }
    }

    [Fact]
    public async Task WhenAdminAndPaymentExists_ReturnsOkWithPayment()
    {
        var seeded = await PaymentSeed.InsertRequestedAsync(DbContext, App.FakeTime.GetUtcNow());

        var (response, payload) = await HttpClientRegistry.AdminClient
            .GETAsync<GetPaymentByIdEndpoint, GetPaymentByIdRequest, GetPaymentByIdResponse>(
                new GetPaymentByIdRequest { PaymentId = seeded.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.PaymentId.Should().Be(seeded.Id);
            payload.CorrelationId.Should().Be(seeded.CorrelationId);
            payload.OrderId.Should().Be(seeded.OrderId);
            payload.BuyerId.Should().Be(seeded.BuyerId);
            payload.Status.Should().Be("Requested");
            payload.Amount.Should().Be(seeded.Amount.Amount);
            payload.Currency.Should().Be(seeded.Amount.Currency.Name);
            // ADR-0011 — response masks sensitive tokens to last-4 (see PaymentTransactionResponseMapper.MaskTrailing).
            // Default seed paymentMethodId is "pm_test_card_visa" → "****visa".
            payload.PaymentMethodId.Should().Be("****visa");
            payload.GatewayTransactionId.Should().BeNull();
            payload.AuthorizedAtUtc.Should().BeNull();
        }
    }
}
