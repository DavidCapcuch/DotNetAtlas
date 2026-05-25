using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FastEndpoints;
using Invoicing.API.Endpoints.Invoices.ResendInvoice;
using Invoicing.FunctionalTests.Common;
using Invoicing.FunctionalTests.Common.TestClientInfrastructure;

namespace Invoicing.FunctionalTests.ApiEndpoints.Invoices;

[Collection<FunctionalTestCollection>]
public class ResendInvoiceTests : BaseApiTest
{
    public ResendInvoiceTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenIdempotencyKeyHeaderMissing_ReturnsBadRequest()
    {
        // ADR-0013 § Header contract: a protected endpoint without the Idempotency-Key
        // header surfaces as 400 from FastEndpoints' .Idempotency() endpoint-level
        // filter (runs after routing + auth + policy gating, before the handler body).
        // Use the AdminClient so the policy gate at the endpoint passes, leaving the
        // missing-header path as the only failure mode.
        var response = await HttpClientRegistry.AdminClient
            .PostAsJsonAsync(
                $"/api/v1/invoicing/invoices/{Guid.CreateVersion7()}/resend",
                new { },
                TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var response = await PostResendAsync(
            HttpClientRegistry.NonAuthClient,
            Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenBuyerWithoutAdminRole_ReturnsForbidden()
    {
        var seed = new InvoiceSeed(DbContext, App.FakeTime);
        var invoice = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);

        var response = await PostResendAsync(
            HttpClientRegistry.BuyerClient,
            invoice.Id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WhenInvoiceUnknown_ReturnsNotFound()
    {
        var response = await PostResendAsync(
            HttpClientRegistry.AdminClient,
            Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenAdminResendsIssuedInvoice_ReturnsNoContent()
    {
        var seed = new InvoiceSeed(DbContext, App.FakeTime);
        var invoice = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);

        var response = await PostResendAsync(
            HttpClientRegistry.AdminClient,
            invoice.Id);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task WhenInvoiceIsDraft_ReturnsConflict()
    {
        // Draft invoices have no PDF / number — resend has nothing to do.
        var seed = new InvoiceSeed(DbContext, App.FakeTime);
        var invoice = await seed.CreateDraftInvoiceAsync(TestUsers.BuyerId);

        var response = await PostResendAsync(
            HttpClientRegistry.AdminClient,
            invoice.Id);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task WhenSameIdempotencyKeyReplayed_ReturnsCachedNoContent()
    {
        var seed = new InvoiceSeed(DbContext, App.FakeTime);
        var invoice = await seed.CreateIssuedInvoiceAsync(TestUsers.BuyerId);

        var key = Guid.NewGuid().ToString();

        var first = await PostResendAsync(HttpClientRegistry.AdminClient, invoice.Id, key);
        var second = await PostResendAsync(HttpClientRegistry.AdminClient, invoice.Id, key);

        // Both return 204; the second is served from the Redis-backed output cache.
        // Pre-cancellation the assertion is structural — for a richer "handler invoked
        // exactly once" check, M9+ can introduce a counter (e.g., a custom middleware
        // wrapper). The current minimal handler is a no-op anyway, so a counter would
        // not differentiate. The 204+204 pair is sufficient evidence that the cache
        // path returns the right shape.
        using (new AssertionScope())
        {
            first.StatusCode.Should().Be(HttpStatusCode.NoContent);
            second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task OpenApiDescription_DisclosesV1StubBehaviour()
    {
        // Wave 1 closeout follow-up H2: the resend endpoint returns 204 + caches
        // the no-op result under Idempotency-Key for 24 h, while the actual
        // invoice_delivery_log insert + outbox row are deferred (see
        // ResendInvoiceCommandHandler xmldoc). Admin tooling reading the OpenAPI
        // spec must see this disclosure so a future maintainer cannot interpret
        // 204 as "delivery performed". The v1-stub marker `(v1 stub)` is the
        // stable contract surface; removing it should fail this test.
        using var response = await HttpClientRegistry.AdminClient.GetAsync(
            "/swagger/v1/swagger.json",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "swagger document must be served in non-production environments per PresentationDependencyInjection.UseInvoicingFastEndpoints");

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        // Match by suffix rather than by exact path string. FastEndpoints normalises
        // route-template parameter casing in its OpenAPI emission (lower-camel-cases
        // `{InvoiceId}` to `{invoiceId}` in v8+), and the spec is unstable across major
        // bumps. The stable contract surface is the operation description ("v1 stub"),
        // not the URL casing.
        doc.RootElement.TryGetProperty("paths", out var paths).Should().BeTrue(
            "swagger document must include a 'paths' object; without it, no versioned " +
            "endpoint is being published — check SwaggerDocument's MaxEndpointVersion " +
            "in PresentationDependencyInjection.");

        JsonElement? resendOperation = null;
        foreach (var path in paths.EnumerateObject())
        {
            if (!path.Name.EndsWith("/resend", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var verb in path.Value.EnumerateObject())
            {
                if (string.Equals(verb.Name, "post", StringComparison.OrdinalIgnoreCase))
                {
                    resendOperation = verb.Value;
                    break;
                }
            }

            if (resendOperation is not null)
            {
                break;
            }
        }

        resendOperation.Should().NotBeNull(
            "the swagger document must include a POST .../resend operation");

        var description = resendOperation.Value.TryGetProperty("description", out var d) ? d.GetString() : null;
        description.Should().NotBeNullOrEmpty(
            "the resend endpoint must publish a description so admin tooling can read its v1 semantics");
        description!.Should().Contain("v1 stub",
            "OpenAPI consumers must see a stable v1-stub disclosure so a 204 acknowledgement is not mistaken for delivery — see ResendInvoiceCommandHandler.cs xmldoc");
    }

    private static async Task<HttpResponseMessage> PostResendAsync(
        HttpClient client,
        Guid invoiceId,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/invoicing/invoices/{invoiceId}/resend")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
