using System.Net;
using FastEndpoints;
using Invoicing.API.Endpoints.Invoices.GetInvoiceByOrderId;
using Invoicing.Application.Invoices.GetInvoiceById;
using Invoicing.FunctionalTests.Common;
using Invoicing.FunctionalTests.Common.TestClientInfrastructure;

namespace Invoicing.FunctionalTests.ApiEndpoints.Invoices;

[Collection(nameof(FunctionalTestCollection))]
public class GetInvoiceByOrderIdTests : BaseApiTest
{
    public GetInvoiceByOrderIdTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenInvoiceForOrderUnknown_ReturnsNotFound()
    {
        var (response, _) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetInvoiceByOrderIdEndpoint, GetInvoiceByOrderIdRequest, ProblemDetails>(
                new GetInvoiceByOrderIdRequest { OrderId = Guid.CreateVersion7() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenBuyerLooksUpOwnInvoiceByOrderId_ReturnsOk()
    {
        var seed = new InvoiceSeed(DbContext, App.FakeTime);
        var orderId = Guid.CreateVersion7();
        var invoice = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId, orderId);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetInvoiceByOrderIdEndpoint, GetInvoiceByOrderIdRequest, GetInvoiceByIdResponse>(
                new GetInvoiceByOrderIdRequest { OrderId = orderId });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.InvoiceId.Should().Be(invoice.Id);
            payload.OrderId.Should().Be(orderId);
            payload.BuyerId.Should().Be(TestUsers.BuyerId);
        }
    }

    [Fact]
    public async Task WhenOtherBuyerLooksUpAnothersInvoiceByOrderId_ReturnsNotFound()
    {
        var seed = new InvoiceSeed(DbContext, App.FakeTime);
        var orderId = Guid.CreateVersion7();
        await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId, orderId);

        var (response, _) = await HttpClientRegistry.OtherBuyerClient
            .GETAsync<GetInvoiceByOrderIdEndpoint, GetInvoiceByOrderIdRequest, ProblemDetails>(
                new GetInvoiceByOrderIdRequest { OrderId = orderId });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
