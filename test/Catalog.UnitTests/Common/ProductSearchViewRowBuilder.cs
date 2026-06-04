using Catalog.Application.Common.ReadModels;
using Catalog.Application.Products.CreateProduct;
using Catalog.Domain.Products.ValueObjects;

namespace Catalog.UnitTests.Common;

public static class ProductSearchViewRowBuilder
{
    public static ProductSearchViewRow Active(
        string sku = "ROW-001",
        string name = "Row Widget",
        string categoryPath = "/electronics",
        decimal amount = 9.99m,
        string currency = "USD",
        Guid? categoryId = null,
        Guid? productId = null)
    {
        return new ProductSearchViewRow
        {
            ProductId = productId ?? Guid.CreateVersion7(),
            Sku = sku,
            Name = name,
            Description = "desc",
            CategoryId = categoryId ?? Guid.CreateVersion7(),
            CategoryPath = categoryPath,
            CategoryBreadcrumb = categoryPath.Replace("/", " > ", StringComparison.Ordinal).Trim(' ', '>'),
            BrandName = "Acme",
            PriceAmount = amount,
            PriceCurrency = currency,
            Status = ProductStatus.Active.Name,
            DimensionsJson = null,
            ImagesJson = "[]",
            IsSellable = true,
            CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            LastUpdatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }

    public static ProductSearchViewRow Discontinued(
        string sku = "ROW-002",
        string categoryPath = "/electronics",
        Guid? categoryId = null)
    {
        var row = Active(sku: sku, categoryPath: categoryPath, categoryId: categoryId);
        row.Status = ProductStatus.Discontinued.Name;
        row.IsSellable = false;
        return row;
    }

    public static ProductSearchViewRow WithImages(this ProductSearchViewRow row, params ImageReferenceDto[] images)
    {
        row.ImagesJson = ProductSearchViewMapper.SerializeImages(images);
        return row;
    }
}
