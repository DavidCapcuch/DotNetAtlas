using System.Net;
using FastEndpoints;
using Invoicing.Api.Endpoints.Invoices.GetInvoicesByBuyer;
using Invoicing.Application.Invoices.GetInvoicesByBuyer;
using Invoicing.FunctionalTests.Common;
using Invoicing.FunctionalTests.Common.TestClientInfrastructure;
using Microsoft.Extensions.Time.Testing;

namespace Invoicing.FunctionalTests.ApiEndpoints.Invoices;

[Collection<FunctionalTestCollection>]
public class GetInvoicesByBuyerTests : BaseApiTest
{
    // ADR-0015: per-test-class pin so the tie-break-by-Id-desc assertion (which relies
    // on two seeded invoices sharing an identical IssueDate) stays deterministic.
    private static readonly DateTimeOffset PinnedNow =
        new(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

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
        var seed = new InvoiceSeed(DbContext, new FakeTimeProvider(PinnedNow));
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

            // Tie-break by Id desc when IssueDate is equal (PinnedNow is shared across
            // both Create calls on the same seed instance, so both own invoices share the
            // same IssueDate). Guid v7 ids are time-ordered, so secondOwn (newer Guid)
            // should come before firstOwn.
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

    [Fact]
    public async Task WhenAdminPassesBuyerIdQuery_ReturnsThatBuyersInvoices()
    {
        // M6 / closeout1: admin override. Admin tooling can list a specific buyer's
        // invoices by passing ?buyerId={guid}. Mirrors the IsAdmin relaxation that
        // GetInvoiceById / GetInvoiceByOrderId / GetCreditNoteById already honour.
        var seed = new InvoiceSeed(DbContext, new FakeTimeProvider(PinnedNow));
        var targetInvoice = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);
        await seed.CreateIssuedInvoiceAsync(TestUsers.OtherBuyerId);

        var (response, payload) = await HttpClientRegistry.AdminClient
            .GETAsync<GetInvoicesByBuyerEndpoint, GetInvoicesByBuyerRequest, GetInvoicesByBuyerResponse>(
                new GetInvoicesByBuyerRequest { BuyerId = TestUsers.BuyerId, Skip = 0, Take = 20 });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Invoices.Should().OnlyContain(i => i.BuyerId == TestUsers.BuyerId);
            payload.Invoices.Select(i => i.InvoiceId).Should().Contain(targetInvoice.Id);
        }
    }

    [Fact]
    public async Task WhenNonAdminPassesOtherBuyerIdQuery_ReturnsForbidden()
    {
        // Non-admin callers that try to scope to a buyer other than themselves get an
        // explicit 403 — not a silent fall-through to caller-scope — so misuse by admin
        // tooling without an admin token surfaces loudly.
        var seed = new InvoiceSeed(DbContext, new FakeTimeProvider(PinnedNow));
        await seed.CreateIssuedInvoiceAsync(TestUsers.OtherBuyerId);

        var (response, _) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetInvoicesByBuyerEndpoint, GetInvoicesByBuyerRequest, ProblemDetails>(
                new GetInvoicesByBuyerRequest { BuyerId = TestUsers.OtherBuyerId, Skip = 0, Take = 20 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenNonAdminPassesOwnBuyerIdQuery_ReturnsTheirInvoices()
    {
        // A buyer redundantly passing their own buyerId must work — there's no
        // boundary crossed.
        var seed = new InvoiceSeed(DbContext, new FakeTimeProvider(PinnedNow));
        var own = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetInvoicesByBuyerEndpoint, GetInvoicesByBuyerRequest, GetInvoicesByBuyerResponse>(
                new GetInvoicesByBuyerRequest { BuyerId = TestUsers.BuyerId, Skip = 0, Take = 20 });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Invoices.Select(i => i.InvoiceId).Should().Contain(own.Id);
        }
    }
}
