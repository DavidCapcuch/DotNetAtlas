using Catalog.Application.Categories.Common.Services;
using Catalog.Application.Common.Data;
using Catalog.Application.Common.ReadModels;
using Catalog.Application.Products.CreateProduct;
using Catalog.Domain.Products.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Catalog.Application.Products.CreateProduct;

/// <summary>
/// Projection handler: builds a fresh <see cref="ProductSearchViewRow"/> on
/// <see cref="ProductCreatedDomainEvent"/>. Runs inside the command's UoW so the row lands in
/// the same <c>SaveChangesAsync</c> call as the aggregate itself.
/// </summary>
public sealed class ProductCreatedProjectionDomainEventHandler : IDomainEventHandler<ProductCreatedDomainEvent>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<ProductCreatedProjectionDomainEventHandler> _logger;

    public ProductCreatedProjectionDomainEventHandler(
        ICatalogDbContext db,
        ILogger<ProductCreatedProjectionDomainEventHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(ProductCreatedDomainEvent domainEvent, CancellationToken ct)
    {
        var product = await _db.Products.FindAsync([domainEvent.ProductId], ct)
            ?? throw new DataIntegrityException(
                "Catalog.ProjectionMissingProduct",
                $"Expected Product '{domainEvent.ProductId}' to be tracked when projecting ProductCreatedDomainEvent, but it was not found.");

        var category = await _db.Categories.FindAsync([product.CategoryId], ct)
            ?? throw new DataIntegrityException(
                "Catalog.ProjectionMissingCategory",
                $"Category '{product.CategoryId}' referenced by product '{product.Id}' was not found in the write model.");

        var images = product.Images.Select(i => new ImageReferenceDto
        {
            Url = i.Url,
            AltText = i.AltText,
            DisplayOrder = i.DisplayOrder,
        }).ToList();

        var dimensions = product.Dimensions is null
            ? null
            : new DimensionsDto
            {
                Length = product.Dimensions.Length,
                Width = product.Dimensions.Width,
                Height = product.Dimensions.Height,
                Unit = product.Dimensions.Unit,
            };

        var row = new ProductSearchViewRow
        {
            ProductId = product.Id,
            Sku = product.Sku.Value,
            Name = product.Name.Value,
            Description = product.Description.Value,
            CategoryId = product.CategoryId,
            CategoryPath = category.Path.Value,
            CategoryBreadcrumb = CategoryBreadcrumbBuilder.Build(category.Path.Value),
            BrandName = product.Brand.Value,
            PriceAmount = product.Price.Amount,
            PriceCurrency = product.Price.Currency.Name,
            Status = product.Status.Name,
            DimensionsJson = ProductSearchViewMapper.SerializeDimensions(dimensions),
            ImagesJson = ProductSearchViewMapper.SerializeImages(images),
            IsSellable = product.Status.IsSellable,
            CreatedAtUtc = product.CreatedUtc,
            LastUpdatedAtUtc = product.LastModifiedUtc,
        };

        _db.ProductSearchView.Add(row);

        _logger.LogDebug(
            "Projected ProductCreatedDomainEvent to product_search_view for Product {ProductId}",
            product.Id);
    }
}
