using Bogus;
using Catalog.Products;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;

namespace Platform.OutboxRelay.Benchmark.Seed;

/// <summary>
/// Bogus faker for generating realistic <see cref="ProductCreatedEvent"/> Avro messages.
/// Generates only the Avro event - serialization to OutboxMessage happens externally.
/// </summary>
public sealed class ProductCreatedEventFaker : Faker<ProductCreatedEvent>
{
    private static readonly string[] Currencies = ["USD", "EUR", "GBP", "CZK", "JPY"];

    public ProductCreatedEventFaker()
    {
        RuleFor(e => e.ProductId, f => f.Random.Guid());
        RuleFor(e => e.Sku, f => $"{f.Random.AlphaNumeric(4).ToUpperInvariant()}-{f.Random.Number(10_000, 99_999)}");
        RuleFor(e => e.Name, f => f.Commerce.ProductName());
        RuleFor(e => e.Description, f => f.Lorem.Sentence(12));
        RuleFor(e => e.CategoryId, f => f.Random.Guid());
        RuleFor(e => e.CategoryPath, f => "/" + string.Join('/', f.Commerce.Categories(2)));
        RuleFor(e => e.BrandName, f => f.Company.CompanyName());
        // PriceAmount's Avro logicalType is decimal(precision 19, scale 4); ToAvroDecimal scales to
        // exactly 4 so serialization doesn't fail on a scale mismatch (see AvroDecimalExtensions).
        RuleFor(e => e.PriceAmount, f => f.Random.Decimal(1m, 5_000m).ToAvroDecimal(4));
        RuleFor(e => e.PriceCurrency, f => f.PickRandom(Currencies));
        RuleFor(e => e.Status, f => f.PickRandom<ProductStatus>());
        RuleFor(e => e.CreatedAtUtc, f => DateTime.UtcNow.AddMinutes(-f.Random.Int(0, 100_000)));
    }
}
