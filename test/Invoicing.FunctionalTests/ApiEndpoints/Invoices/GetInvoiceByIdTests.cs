using System.Net;
using FastEndpoints;
using Invoicing.API.Endpoints.Invoices.GetInvoiceById;
using Invoicing.Application.Invoices.GetInvoiceById;
using Invoicing.FunctionalTests.Common;
using Invoicing.FunctionalTests.Common.TestClientInfrastructure;

namespace Invoicing.FunctionalTests.ApiEndpoints.Invoices;

[Collection<FunctionalTestCollection>]
public class GetInvoiceByIdTests : BaseApiTest
{
    public GetInvoiceByIdTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClientRegistry.NonAuthClient
            .GETAsync<GetInvoiceByIdEndpoint, GetInvoiceByIdRequest, GetInvoiceByIdResponse>(
                new GetInvoiceByIdRequest { InvoiceId = Guid.CreateVersion7() });

        response.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenInvoiceUnknown_ReturnsNotFound()
    {
        var (response, _) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetInvoiceByIdEndpoint, GetInvoiceByIdRequest, ProblemDetails>(
                new GetInvoiceByIdRequest { InvoiceId = Guid.CreateVersion7() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenBuyerReadsOwnInvoice_ReturnsOkWithPresignedUrl()
    {
        var seed = new InvoiceSeed(DbContext, App.FakeTime);
        var invoice = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetInvoiceByIdEndpoint, GetInvoiceByIdRequest, GetInvoiceByIdResponse>(
                new GetInvoiceByIdRequest { InvoiceId = invoice.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.InvoiceId.Should().Be(invoice.Id);
            payload.BuyerId.Should().Be(TestUsers.BuyerId);
            payload.Status.Should().Be("Issued");
            payload.InvoiceNumber.Should().NotBeNullOrEmpty();
            payload.PdfPresignedUrl.Should().NotBeNull();
            payload.PdfPresignedUrlExpiresAtUtc.Should().NotBeNull();
            payload.Lines.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task WhenAdminReadsAnotherBuyersInvoice_ReturnsOk()
    {
        var seed = new InvoiceSeed(DbContext, App.FakeTime);
        var invoice = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);

        var (response, payload) = await HttpClientRegistry.AdminClient
            .GETAsync<GetInvoiceByIdEndpoint, GetInvoiceByIdRequest, GetInvoiceByIdResponse>(
                new GetInvoiceByIdRequest { InvoiceId = invoice.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.BuyerId.Should().Be(TestUsers.BuyerId);
        }
    }

    [Fact]
    public async Task WhenOtherBuyerReadsAnothersInvoice_ReturnsNotFound()
    {
        var seed = new InvoiceSeed(DbContext, App.FakeTime);
        var invoice = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);

        var (response, _) = await HttpClientRegistry.OtherBuyerClient
            .GETAsync<GetInvoiceByIdEndpoint, GetInvoiceByIdRequest, ProblemDetails>(
                new GetInvoiceByIdRequest { InvoiceId = invoice.Id });

        // Existence must NOT be leaked — cross-buyer reads surface as 404, not 403.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
