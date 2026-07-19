using System.Net;
using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.IntegrationTests.Common;
using Catalog.Products;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.Test.Framework.Assertions;

namespace Catalog.IntegrationTests.Contracts;

/// <summary>
/// Verifies the domain-event handler chain routes an outbox row to the right Avro contract
/// type and Kafka topic. This pins the wiring (handler registered, publisher fires, row
/// lands with the correct CLR type FQN + topic name). The fixture uses
/// <see cref="FakeOutboxWriter"/> to bypass Schema Registry, so the AvroPayload column is
/// empty — this pins the routing, not the serialized bytes.
/// </summary>
[Collection<IntegrationTestCollection>]
public class OutboxPublisherRoutingTests : BaseIntegrationTest
{
    public OutboxPublisherRoutingTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task CreateProduct_RoutesOutboxRowToCorrectAvroTypeAndTopic()
    {
        var (_, cat) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateCategoryEndpoint, CreateCategoryRequest, CreateCategoryResponse>(
                CatalogTestData.ValidCreateCategoryRequest());

        var (response, body) = await HttpClientRegistry.WriteClient
            .POSTAsync<CreateProductEndpoint, CreateProductRequest, CreateProductResponse>(
                CatalogTestData.ValidCreateProductRequest(cat.CategoryId));

        using (new AssertionScope())
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var row = await DbContext.Set<OutboxMessage>()
                .Where(m => m.KafkaKey == body.ProductId.ToString())
                .OrderByDescending(m => m.CreatedUtc)
                .FirstAsync(TestContext.Current.CancellationToken);

            row.Type.Should().BeMessageType<ProductCreatedEvent>();
            row.TopicName.Should().Be("catalog.products");
        }
    }
}
