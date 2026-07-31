using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
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
    private const string InvoicesListRoute = "/api/v1/invoicing/invoices";

    // Positive control for the requiredness assertions — a path parameter is required by
    // construction, so it proves the document still expresses requiredness at all.
    private const string InvoiceByIdRoute = "/api/v1/invoicing/invoices/{invoiceId}";

    // ADR-0015: per-test-class pin so the tie-break-by-Id-desc assertion (which relies
    // on two seeded invoices sharing an identical IssueDate) stays deterministic.
    private static readonly DateTimeOffset PinnedNow =
        new(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

    public GetInvoicesByBuyerTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var (response, _) = await HttpClientRegistry.NonAuthClient
            .GETAsync<GetInvoicesByBuyerEndpoint, GetInvoicesByBuyerRequest, GetInvoicesByBuyerResponse>(
                new GetInvoicesByBuyerRequest { PageNumber = 1, PageSize = 20 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "critical-path")]
    public async Task WhenBuyerHasInvoices_ReturnsOnlyTheirsMostRecentFirst()
    {
        var seed = new InvoiceSeed(DbContext, new FakeTimeProvider(PinnedNow));
        // Two for the buyer, one for someone else — the response must filter.
        var firstOwn = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);
        var secondOwn = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);
        await seed.CreateIssuedInvoiceAsync(TestUsers.OtherBuyerId);

        var (response, payload) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetInvoicesByBuyerEndpoint, GetInvoicesByBuyerRequest, GetInvoicesByBuyerResponse>(
                new GetInvoicesByBuyerRequest { PageNumber = 1, PageSize = 20 });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Total.Should().Be(2);
            payload.PageNumber.Should().Be(1);
            payload.PageSize.Should().Be(20);
            payload.Items.Should().HaveCount(2);
            payload.Items.Should().OnlyContain(i => i.BuyerId == TestUsers.BuyerId);

            // Tie-break by Id desc when IssueDate is equal (PinnedNow is shared across
            // both Create calls on the same seed instance, so both own invoices share the
            // same IssueDate). Guid v7 ids are time-ordered, so secondOwn (newer Guid)
            // should come before firstOwn.
            payload.Items.Select(i => i.InvoiceId)
                .Should().ContainInOrder(secondOwn.Id, firstOwn.Id);
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task WhenPageSizeOutOfRange_ReturnsBadRequestOrUnprocessable()
    {
        // PageSize=0 violates InclusiveBetween(1, 100). FastEndpoints' validation pipeline +
        // AddProblemDetails maps FluentValidation failures to 400 by default; either 400
        // or 422 is acceptable per the BC's API conventions.
        var (response, _) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetInvoicesByBuyerEndpoint, GetInvoicesByBuyerRequest, ProblemDetails>(
                new GetInvoicesByBuyerRequest { PageNumber = 1, PageSize = 0 });

        ((int)response.StatusCode).Should().BeOneOf(400, 422);
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenAdminPassesBuyerIdQuery_ReturnsThatBuyersInvoices()
    {
        // Admin override: admin tooling can list a specific buyer's invoices by
        // passing ?buyerId={guid}. Mirrors the IsAdmin relaxation that
        // GetInvoiceById / GetInvoiceByOrderId / GetCreditNoteById already honour.
        var seed = new InvoiceSeed(DbContext, new FakeTimeProvider(PinnedNow));
        var targetInvoice = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);
        await seed.CreateIssuedInvoiceAsync(TestUsers.OtherBuyerId);

        var (response, payload) = await HttpClientRegistry.AdminClient
            .GETAsync<GetInvoicesByBuyerEndpoint, GetInvoicesByBuyerRequest, GetInvoicesByBuyerResponse>(
                new GetInvoicesByBuyerRequest { BuyerId = TestUsers.BuyerId, PageNumber = 1, PageSize = 20 });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Items.Should().OnlyContain(i => i.BuyerId == TestUsers.BuyerId);
            payload.Items.Select(i => i.InvoiceId).Should().Contain(targetInvoice.Id);
        }
    }

    [Fact]
    [Trait("Category", "security")]
    public async Task WhenNonAdminPassesOtherBuyerIdQuery_ReturnsForbidden()
    {
        // Non-admin callers that try to scope to a buyer other than themselves get an
        // explicit 403 — not a silent fall-through to caller-scope — so misuse by admin
        // tooling without an admin token surfaces loudly.
        var seed = new InvoiceSeed(DbContext, new FakeTimeProvider(PinnedNow));
        await seed.CreateIssuedInvoiceAsync(TestUsers.OtherBuyerId);

        var (response, _) = await HttpClientRegistry.BuyerClient
            .GETAsync<GetInvoicesByBuyerEndpoint, GetInvoicesByBuyerRequest, ProblemDetails>(
                new GetInvoicesByBuyerRequest { BuyerId = TestUsers.OtherBuyerId, PageNumber = 1, PageSize = 20 });

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
                new GetInvoicesByBuyerRequest { BuyerId = TestUsers.BuyerId, PageNumber = 1, PageSize = 20 });

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            payload.Items.Select(i => i.InvoiceId).Should().Contain(own.Id);
        }
    }

    [Fact]
    [Trait("Category", "boundary")]
    public async Task WhenPagingParamsOmitted_ReturnsFirstPageOfTwenty()
    {
        // The server treats paging as optional and supplies 1/20 itself. Every other test
        // here passes both params explicitly, so nothing else pins that — and the OpenAPI
        // document is generated from the same members, so this is the behaviour the
        // document must agree with (ADR-0038).
        var seed = new InvoiceSeed(DbContext, new FakeTimeProvider(PinnedNow));
        await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);

        using var response = await HttpClientRegistry.BuyerClient.GetAsync(
            InvoicesListRoute,
            TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GetInvoicesByBuyerResponse>(
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(
                HttpStatusCode.OK,
                "omitting pageNumber/pageSize is legal — the server defaults them");
            payload!.PageNumber.Should().Be(1);
            payload.PageSize.Should().Be(20);
            payload.Total.Should().Be(1);
        }
    }

    [Fact]
    public async Task OpenApiDocument_DescribesPagingParamsAsOptional()
    {
        // ADR-0038 turns this document into the committed HTTP contract, read by oasdiff
        // and by generated clients. A parameter marked required that the server defaults
        // makes a generated client demand something the server never asked for.
        var document = await GetDocumentAsync();

        using (new AssertionScope())
        {
            // Positive control first. NSwag omits the `required` key entirely for an optional
            // parameter rather than emitting `required: false`, so every assertion below is
            // satisfied by requiredness disappearing from the document altogether — which is a
            // live risk, since ADR-0038 driver 4 plans the NSwag -> Microsoft.AspNetCore.OpenApi
            // move. A path parameter is required by construction; if this goes false the
            // mechanism is gone and the assertions below prove nothing.
            IsRequired(ParametersOf(document, InvoiceByIdRoute), "invoiceId").Should().BeTrue(
                "a path parameter is always required — this pins that the document still "
                + "expresses requiredness at all");

            var parameters = ParametersOf(document, InvoicesListRoute);
            IsRequired(parameters, "pageNumber").Should().BeFalse();
            IsRequired(parameters, "pageSize").Should().BeFalse();
        }
    }

    [Theory]
    [Trait("Category", "boundary")]
    [InlineData("?pageSize=")]
    [InlineData("?pageSize=abc")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageNumber=0")]
    public async Task WhenPagingParamIsPresentButUnusable_RejectsRatherThanFallingBackToTheDefault(
        string queryString)
    {
        // An omitted param takes the default; a *supplied* one never does. `?pageSize=` binds to
        // 0 rather than null, so the endpoint's `??` deliberately does not fire and the value is
        // rejected by the validator — a caller's typo must not be silently served as page 1 of 20.
        using var response = await HttpClientRegistry.BuyerClient.GetAsync(
            InvoicesListRoute + queryString,
            TestContext.Current.CancellationToken);

        ((int)response.StatusCode).Should().BeOneOf(400, 422);
    }

    [Fact]
    public async Task OpenApiDocument_PublishesThePagingDefaults()
    {
        // "Optional" alone leaves a consumer guessing what it gets by omitting the param.
        // The document carries the actual fallback so a generated client and its reader
        // both see it.
        var parameters = ParametersOf(await GetDocumentAsync(), InvoicesListRoute);

        using (new AssertionScope())
        {
            DefaultOf(parameters, "pageNumber").Should().Be(1);
            DefaultOf(parameters, "pageSize").Should().Be(20);
        }
    }

    private static int? DefaultOf(JsonArray parameters, string name)
        => ParameterNamed(parameters, name)["schema"]?["default"]?.GetValue<int>();

    private static bool IsRequired(JsonArray parameters, string name)
        => ParameterNamed(parameters, name)["required"]?.GetValue<bool>() ?? false;

    private static JsonNode ParameterNamed(JsonArray parameters, string name)
        => parameters.SingleOrDefault(p => p!["name"]!.GetValue<string>() == name)
            ?? throw new InvalidOperationException(
                $"The document declares no '{name}' parameter. Declared: "
                + string.Join(", ", parameters.Select(p => p!["name"]!.GetValue<string>())));

    private static JsonArray ParametersOf(JsonNode document, string route)
        => document["paths"]![route]!["get"]!["parameters"]!.AsArray();

    private async Task<JsonNode> GetDocumentAsync()
    {
        var document = await HttpClientRegistry.BuyerClient.GetStringAsync(
            "/swagger/v1/swagger.json",
            TestContext.Current.CancellationToken);

        return JsonNode.Parse(document)!;
    }
}
