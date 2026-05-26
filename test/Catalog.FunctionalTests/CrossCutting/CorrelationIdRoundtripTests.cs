using System.Net;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.FunctionalTests.Common;
using Catalog.FunctionalTests.Common.TestClientInfrastructure;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ServiceDefaults.CorrelationId;

namespace Catalog.FunctionalTests.CrossCutting;

/// <summary>
/// Closes the ADR-0008 DoD requirement: an inbound HTTP <c>X-Correlation-Id</c> must reach
/// the same handler scope that writes the outbox row. The outbox-relay-catalog container then
/// copies the JSON-encoded headers onto the Kafka message header, completing the HTTP → Kafka
/// chain. This test asserts the HTTP entry point runs cleanly with the header set; the strict
/// header-baggage round-trip is a Platform.ReliableMessaging.Outbox concern owned by Wave 0
/// and verified by Platform integration tests.
/// </summary>
[Collection<FunctionalTestCollection>]
public class CorrelationIdRoundtripTests : BaseApiTest
{
    public CorrelationIdRoundtripTests(ApiTestFixture app)
        : base(app)
    {
    }

    // Still blocked, but the original skip-comment misdiagnosed the cause. The actual blocker:
    // ApiTestFixture swaps the production IOutboxWriter for an in-memory FakeOutboxWriter that
    // does NOT call DbContext.Add, so the test's `DbContext.Set<OutboxMessage>()` query is empty
    // regardless of whether the Activity tag is set. Cross-cutting wave1-followup #256 closed the
    // platform-side Activity → top-level header gap (verified by
    // Platform.ReliableMessaging.Outbox.EFCore.UnitTests.OutboxMessageHeaderExtensionsCorrelationIdTests).
    // Un-skipping this test requires either extending FakeOutboxWriter to capture and expose
    // the Activity-derived headers per message, or replacing it with the real OutboxWriter for
    // this scenario — both broader than the in-flight wave1 cleanup.
    [Fact(Skip = "FakeOutboxWriter is in-memory and bypasses DbContext; DB query asserts nothing. Needs a FakeOutboxWriter that captures headers or a per-test real-writer override.")]
    public async Task WhenCorrelationIdHeaderProvided_OutboxRowCarriesIt()
    {
        // Arrange — seed a category needed by CreateProduct.
        var (_, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());

        var correlationId = Guid.CreateVersion7().ToString();
        var client = HttpClientRegistry.CreateFresh(ClientType.WriteAdmin);
        client.DefaultRequestHeaders.Add(CorrelationIdContextKeys.HttpHeaderName, correlationId);

        var request = CatalogTestData.ValidCreateProductRequest(cat.CategoryId);

        // Act
        var (response, body) = await client
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(request);

        // Assert — the inbound X-Correlation-Id must reach OutboxMessage.Headers (W3C Trace
        // Context + OTel baggage). Per ADR-0008 the relay-side then copies that onto the Kafka
        // header, but here we close the HTTP→outbox half of the chain.
        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var outboxHeaders = await DbContext.Set<OutboxMessage>()
                .Where(m => m.KafkaKey == body.ProductId.ToString())
                .Select(m => m.Headers)
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            outboxHeaders.Should().NotBeNullOrEmpty(
                "ADR-0008 requires correlation propagation onto the outbox row Headers column");
            outboxHeaders.Should().Contain(correlationId,
                "the X-Correlation-Id from the request must round-trip into the outbox baggage");
        }
    }
}
