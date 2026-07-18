using Catalog.Api.Endpoints.Categories.CreateCategory;
using Catalog.Api.Endpoints.Products.CreateProduct;
using Catalog.Application.Common.Contracts;

namespace Catalog.IntegrationTests.Common;

/// <summary>
/// Canonical request payloads used across integration tests. Keeps test bodies focused on the
/// behaviour under test rather than fiddly DTO construction.
/// </summary>
internal static class CatalogTestData
{
    public static CreateProductRequest ValidCreateProductRequest(
        Guid categoryId,
        string? sku = null,
        string? name = null,
        decimal amount = 19.99m,
        string currency = "EUR")
    {
        // CreateVersion7 emits time-ordered Guids whose hex prefix collides across consecutive
        // calls in the same millisecond — use the suffix slice for SKU uniqueness instead.
        var seed = Guid.CreateVersion7().ToString("N").Substring(20, 12).ToUpperInvariant();
        return new CreateProductRequest
        {
            Sku = sku ?? $"SKU-{seed}",
            Name = name ?? $"Widget-{seed}",
            Description = "A premium widget.",
            CategoryId = categoryId,
            Brand = "Acme",
            Price = new MoneyDto { Amount = amount, Currency = currency },
            Dimensions = new DimensionsDto { Length = 10m, Width = 5m, Height = 2m, Unit = "cm" },
            Images =
            [
                new ImageReferenceDto
                {
                    Url = "https://cdn.dotnetatlas.test/widget.png",
                    AltText = "Widget photo",
                    DisplayOrder = 0,
                },
            ],
        };
    }

    public static CreateCategoryRequest ValidCreateCategoryRequest(
        string? name = null,
        Guid? parentCategoryId = null)
    {
        var seed = Guid.CreateVersion7().ToString("N").Substring(0, 8).ToUpperInvariant();
        return new CreateCategoryRequest
        {
            Name = name ?? $"Category-{seed}",
            ParentCategoryId = parentCategoryId,
        };
    }
}
