using System.Net;
using FastEndpoints;
using Invoicing.API.Endpoints.Invoices.GetInvoicesByBuyer;
using Invoicing.Application.Invoices.GetInvoicesByBuyer;
using Invoicing.FunctionalTests.Common;
using Invoicing.FunctionalTests.Common.TestClientInfrastructure;

namespace Invoicing.FunctionalTests.ApiEndpoints.Invoices;

[Collection(nameof(FunctionalTestCollection))]
public class GetInvoicesByBuyerTests : BaseApiTest
{
    public GetInvoicesByBuyerTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var (response, _) = await HttpClientRegistry.NonAuthClient
            .GETAsync<GetInvoicesByBuyerEndpoint, GetInvoicesByBuyerRequest, GetInvoicesByBuyerResponse>(
                new GetInvoicesByBuyerRequest { Skip = 0, Take = 20 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenBuyerHasInvoices_ReturnsOnlyTheirsMostRecentFirst()
    {
        var seed = new InvoiceSeed(DbContext, App.FakeTime);
        // Two for the buyer, one for someone else — the response must filter.
        var firstOwn = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);
        var secondOwn = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);
        await seed.CreateIssuedInvoiceAsync(TestUsers.OtherBuyerId);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetInvoicesByBuyerEndpoint, GetInvoicesByBuyerRequest, GetInvoicesByBuyerResponse>(
                new GetInvoicesByBuyerRequest { Skip = 0, Take = 20 });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Invoices.Should().HaveCount(2);
            payload.Invoices.Should().OnlyContain(i => i.BuyerId == TestUsers.BuyerId);

            // Tie-break by Id desc when IssueDate is equal (FakeTime is pinned, so both
            // own invoices share the same IssueDate). Guid v7 ids are time-ordered, so
            // secondOwn (newer Guid) should come before firstOwn.
            payload.Invoices.Select(i => i.InvoiceId)
                .Should().ContainInOrder(secondOwn.Id, firstOwn.Id);
        }
    }

    [Fact]
    public async Task WhenTakeOutOfRange_ReturnsBadRequestOrUnprocessable()
    {
        // Take=0 violates InclusiveBetween(1, 100). FastEndpoints' validation pipeline +
        // AddProblemDetails maps FluentValidation failures to 400 by default; either 400
        // or 422 is acceptable per the BC's API conventions.
        var (response, _) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetInvoicesByBuyerEndpoint, GetInvoicesByBuyerRequest, ProblemDetails>(
                new GetInvoicesByBuyerRequest { Skip = 0, Take = 0 });

        ((int)response.StatusCode).Should().BeOneOf(400, 422);
    }
}
