using System.Net;
using FastEndpoints;
using Invoicing.Api.Endpoints.CreditNotes.GetCreditNoteById;
using Invoicing.Application.CreditNotes.GetCreditNoteById;
using Invoicing.FunctionalTests.Common;
using Invoicing.FunctionalTests.Common.TestClientInfrastructure;
using Microsoft.Extensions.Time.Testing;

namespace Invoicing.FunctionalTests.ApiEndpoints.CreditNotes;

[Collection<FunctionalTestCollection>]
public class GetCreditNoteByIdTests : BaseApiTest
{
    // ADR-0015: per-test-class pin so seeded IssueDate / credit-note number year stay
    // deterministic.
    private static readonly DateTimeOffset PinnedNow =
        new(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

    public GetCreditNoteByIdTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var response = await HttpClientRegistry.NonAuthClient
            .GETAsync<GetCreditNoteByIdEndpoint, GetCreditNoteByIdRequest, GetCreditNoteByIdResponse>(
                new GetCreditNoteByIdRequest { CreditNoteId = Guid.CreateVersion7() });

        response.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenCreditNoteUnknown_ReturnsNotFound()
    {
        var (response, _) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetCreditNoteByIdEndpoint, GetCreditNoteByIdRequest, ProblemDetails>(
                new GetCreditNoteByIdRequest { CreditNoteId = Guid.CreateVersion7() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task WhenBuyerReadsOwnCreditNote_ReturnsOk()
    {
        var seed = new InvoiceSeed(DbContext, new FakeTimeProvider(PinnedNow));
        var creditNote = await seed.CreateIssuedCreditNoteAsync(TestUsers.BuyerId);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetCreditNoteByIdEndpoint, GetCreditNoteByIdRequest, GetCreditNoteByIdResponse>(
                new GetCreditNoteByIdRequest { CreditNoteId = creditNote.Id });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.CreditNoteId.Should().Be(creditNote.Id);
            payload.BuyerId.Should().Be(TestUsers.BuyerId);
            payload.CreditNoteNumber.Should().NotBeNullOrEmpty();
            payload.OriginalInvoiceNumber.Should().NotBeNullOrEmpty();
            payload.TotalAmount.Should().BeLessThan(0); // I-CN-2: strictly negative.
            payload.PdfPresignedUrl.Should().NotBeNull();
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenOtherBuyerReadsAnothersCreditNote_ReturnsNotFound()
    {
        var seed = new InvoiceSeed(DbContext, new FakeTimeProvider(PinnedNow));
        var creditNote = await seed.CreateIssuedCreditNoteAsync(TestUsers.BuyerId);

        var (response, _) = await HttpClientRegistry.OtherBuyerClient
            .GETAsync<GetCreditNoteByIdEndpoint, GetCreditNoteByIdRequest, ProblemDetails>(
                new GetCreditNoteByIdRequest { CreditNoteId = creditNote.Id });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
