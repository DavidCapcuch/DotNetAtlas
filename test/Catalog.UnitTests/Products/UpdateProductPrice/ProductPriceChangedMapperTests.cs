using Avro;
using Catalog.Application.Products.UpdateProductPrice;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using Platform.SharedKernel.ValueObjects;
using AvroProductPriceChanged = Catalog.Products.ProductPriceChangedEvent;

namespace Catalog.UnitTests.Products.UpdateProductPrice;

/// <summary>
/// Exhaustive mapping coverage for
/// <see cref="ProductPriceChangedMapper.ToProductPriceChangedEvent"/> — the pure leaf projecting
/// the aggregate + domain event onto the external Avro contract.
/// </summary>
public class ProductPriceChangedMapperTests
{
    /// <summary>Scale pinned by the money fields in <c>ProductPriceChangedEvent.avsc</c>.</summary>
    private const int MoneyScale = 4;

    [Fact]
    public void ToProductPriceChangedEvent_WhenPriceChanges_MapsBothAmountsCurrencyAndTimestamp()
    {
        // Arrange
        var product = CatalogFactories.ActiveProduct(sku: "SKU-42");
        var changedAt = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

        // Old/new currencies deliberately differ so the mapped Currency pins the source field
        // (NewPrice, not OldPrice). Two-decimal amounts (not pre-scaled to 4) keep the scale
        // normalisation honest — a raw `new AvroDecimal(x)` would fail the Scale assertions.
        var domainEvent = new ProductPriceChangedDomainEvent
        {
            ProductId = product.Id,
            OldPrice = Money.Create(9.99m, "EUR").Value,
            NewPrice = Money.Create(14.50m, "USD").Value,
            OccurredOnUtc = changedAt,
        };

        // Act
        AvroProductPriceChanged avro = product.ToProductPriceChangedEvent(domainEvent);

        // Assert
        using (new AssertionScope())
        {
            avro.ProductId.Should().Be(product.Id);
            avro.Sku.Should().Be("SKU-42");
            // Scale-comparing oracle, not a (decimal) cast — the cast erases scale, hiding an
            // amount emitted at the input's own scale rather than the schema's 4.
            avro.OldPriceAmount.Should().Be(new AvroDecimal(9.9900m));
            avro.OldPriceAmount.Scale.Should().Be(MoneyScale);
            avro.NewPriceAmount.Should().Be(new AvroDecimal(14.5000m));
            avro.NewPriceAmount.Scale.Should().Be(MoneyScale);
            avro.Currency.Should().Be("USD");
            avro.ChangedAtUtc.Should().Be(changedAt.UtcDateTime);
        }
    }
}
