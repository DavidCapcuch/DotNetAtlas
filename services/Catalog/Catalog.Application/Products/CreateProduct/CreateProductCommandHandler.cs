using Catalog.Application.Common.Data;
using Catalog.Domain.Categories.Errors;
using Catalog.Domain.Products;
using Catalog.Domain.Products.Errors;
using Catalog.Domain.Products.ValueObjects;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.Application.Products.CreateProduct;

/// <summary>
/// Creates a new <see cref="Product"/> in the Catalog. Per <c>use-cases.md § 1.1.1</c>:
/// <list type="number">
///   <item>Fails with <see cref="ProductErrors.SkuAlreadyExists"/> if another product has the same SKU.</item>
///   <item>Fails with <see cref="CategoryErrors.NotFound"/> if the referenced category is missing.</item>
///   <item>Assembles VOs and calls <see cref="Product.Create"/>.</item>
///   <item>Saves the aggregate via the shared <see cref="ICatalogDbContext"/>.</item>
/// </list>
/// Raises <see cref="Catalog.Domain.Products.Events.ProductCreatedDomainEvent"/>, which downstream
/// projection + outbox handlers react to atomically.
/// </summary>
public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<CreateProductCommandHandler> _logger;

    public CreateProductCommandHandler(
        ICatalogDbContext db,
        ILogger<CreateProductCommandHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(CreateProductCommand command, CancellationToken ct)
    {
        var normalizedSku = command.Sku.Trim().ToUpperInvariant();

        var skuExists = await _db.Products
            .AsNoTracking()
            .AnyAsync(p => p.Sku.Value == normalizedSku, ct);
        if (skuExists)
        {
            return Result.Fail<Guid>(ProductErrors.SkuAlreadyExists(normalizedSku));
        }

        var categoryExists = await _db.Categories
            .AsNoTracking()
            .AnyAsync(c => c.Id == command.CategoryId, ct);
        if (!categoryExists)
        {
            return Result.Fail<Guid>(CategoryErrors.NotFound(command.CategoryId));
        }

        var skuResult = Sku.Create(command.Sku);
        if (skuResult.IsFailed)
        {
            return skuResult.ToResult<Guid>();
        }

        var nameResult = ProductName.Create(command.Name);
        if (nameResult.IsFailed)
        {
            return nameResult.ToResult<Guid>();
        }

        var descriptionResult = ProductDescription.Create(command.Description);
        if (descriptionResult.IsFailed)
        {
            return descriptionResult.ToResult<Guid>();
        }

        var brandResult = BrandName.Create(command.Brand);
        if (brandResult.IsFailed)
        {
            return brandResult.ToResult<Guid>();
        }

        var priceResult = Money.Create(command.Price.Amount, command.Price.Currency);
        if (priceResult.IsFailed)
        {
            return priceResult.ToResult<Guid>();
        }

        Dimensions? dimensions = null;
        if (command.Dimensions is not null)
        {
            var dimensionsResult = Dimensions.Create(
                command.Dimensions.Length,
                command.Dimensions.Width,
                command.Dimensions.Height,
                command.Dimensions.Unit);
            if (dimensionsResult.IsFailed)
            {
                return dimensionsResult.ToResult<Guid>();
            }

            dimensions = dimensionsResult.Value;
        }

        var images = new List<ImageReference>(command.Images.Count);
        foreach (var imgDto in command.Images)
        {
            var imageResult = ImageReference.Create(imgDto.Url, imgDto.AltText, imgDto.DisplayOrder);
            if (imageResult.IsFailed)
            {
                return imageResult.ToResult<Guid>();
            }

            images.Add(imageResult.Value);
        }

        var productResult = Product.Create(
            skuResult.Value,
            nameResult.Value,
            descriptionResult.Value,
            command.CategoryId,
            brandResult.Value,
            priceResult.Value,
            dimensions,
            images);

        if (productResult.IsFailed)
        {
            return productResult.ToResult<Guid>();
        }

        var product = productResult.Value;
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created Product {ProductId} with SKU {Sku} in category {CategoryId}",
            product.Id, product.Sku.Value, product.CategoryId);

        return Result.Ok(product.Id);
    }
}
