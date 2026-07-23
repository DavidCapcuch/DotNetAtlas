using Catalog.Application.Products.DiscontinueProduct;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using AvroProductDiscontinuedEvent = Catalog.Products.ProductDiscontinuedEvent;

namespace Catalog.UnitTests.Products.DiscontinueProduct;

/// <summary>
/// Exhaustive mapping coverage for
/// <see cref="ProductDiscontinuedMapper.ToProductDiscontinuedEvent"/> — the pure leaf that
/// projects the aggregate + domain event onto the external Avro contract. Downstream consumers
/// depend on the exact payload, so every field is pinned here.
/// </summary>
public class ProductDiscontinuedMapperTests
{
    [Fact]
    public void ToProductDiscontinuedEvent_WhenDiscontinued_MapsSkuReasonAndTimestamp()
    {
        // Arrange
        var product = CatalogFactories.ActiveProduct(sku: "SKU-42");
        var discontinuedAt = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);
        var domainEvent = new ProductDiscontinuedDomainEvent
        {
            ProductId = product.Id,
            Reason = "Supplier EOL",
            OccurredOnUtc = discontinuedAt,
        };

        // Act
        AvroProductDiscontinuedEvent avro = product.ToProductDiscontinuedEvent(domainEvent);

        // Assert
        using (new AssertionScope())
        {
            avro.ProductId.Should().Be(product.Id);
            avro.Sku.Should().Be("SKU-42");
            avro.Reason.Should().Be("Supplier EOL");
            avro.DiscontinuedAtUtc.Should().Be(discontinuedAt.UtcDateTime);
        }
    }
}
