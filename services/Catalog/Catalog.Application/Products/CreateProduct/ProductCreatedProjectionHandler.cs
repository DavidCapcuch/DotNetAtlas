using System.Diagnostics;
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
public sealed class ProductCreatedProjectionHandler : IDomainEventHandler<ProductCreatedDomainEvent>
{
    // Mirrors Platform.ServiceDefaults.CorrelationId.CorrelationIdContextKeys.ActivityTagName.
    // Inlined to avoid coupling Catalog.Application to Platform.ServiceDefaults. CAT-RV-C01
    // (Wave-1 closeout): the AddCorrelationId middleware (Catalog.API/Program.cs:27) writes
    // the request's correlation id onto Activity.Current via this tag.
    private const string CorrelationIdActivityTag = "correlation.id";

    private readonly ICatalogDbContext _db;
    private readonly ILogger<ProductCreatedProjectionHandler> _logger;

    public ProductCreatedProjectionHandler(
        ICatalogDbContext db,
        ILogger<ProductCreatedProjectionHandler> logger)
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
            CategoryBreadcrumb = BuildBreadcrumb(category.Path.Value),
            BrandName = product.Brand.Value,
            PriceAmount = product.Price.Amount,
            PriceCurrency = product.Price.Currency.Name,
            Status = product.Status.Name,
            DimensionsJson = ProductSearchViewMapper.SerializeDimensions(dimensions),
            ImagesJson = ProductSearchViewMapper.SerializeImages(images),
            IsSellable = product.Status.IsSellable,
            CreatedAtUtc = product.CreatedUtc,
            LastUpdatedAtUtc = product.LastModifiedUtc,

            CorrelationId = ResolveCorrelationId(),
        };

        _db.ProductSearchView.Add(row);

        _logger.LogDebug(
            "Projected ProductCreatedDomainEvent to product_search_view for Product {ProductId}",
            product.Id);
    }

    private static Guid ResolveCorrelationId()
    {
        // Background / inbox-driven flows have no Activity tag — fall back to Guid.Empty
        // rather than manufacture a synthetic id.
        if (Activity.Current?.GetTagItem(CorrelationIdActivityTag) is not string tag)
        {
            return Guid.Empty;
        }

        return Guid.TryParse(tag, out var correlationId) ? correlationId : Guid.Empty;
    }

    private static string BuildBreadcrumb(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" > ", segments.Select(ToHumanReadableSegment));
    }

    // CAT-RV-L01 (Wave-1 closeout): category slug segments contain hyphens between words
    // ("electronics-toys"). Title-case each space-delimited token, not just the first
    // character of the whole segment, so "electronics-toys" -> "Electronics Toys" rather
    // than "Electronics-toys".
    private static string ToHumanReadableSegment(string segment)
    {
        if (segment.Length == 0)
        {
            return segment;
        }

        var tokens = segment.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", tokens.Select(TitleCaseToken));
    }

    private static string TitleCaseToken(string token)
    {
        if (token.Length == 0)
        {
            return token;
        }

        return char.ToUpperInvariant(token[0]) + token[1..];
    }
}
