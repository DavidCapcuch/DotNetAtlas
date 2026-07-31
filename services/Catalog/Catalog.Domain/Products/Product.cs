using Catalog.Domain.Products.Errors;
using Catalog.Domain.Products.Events;
using Catalog.Domain.Products.ValueObjects;
using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.Domain.Products;

/// <summary>
/// Aggregate root representing a sellable item in the Catalog.
/// Owns identity, business key (<see cref="Sku"/>), descriptive content, category reference,
/// commercial terms (<see cref="Price"/>), and lifecycle <see cref="Status"/>.
/// All state changes flow through factory / transition methods and raise domain events.
/// </summary>
/// <remarks>
/// This aggregate can raise the following domain events:
/// <list type="bullet">
/// <item><see cref="ProductCreatedDomainEvent"/>: When a new product is created.</item>
/// <item><see cref="ProductPriceChangedDomainEvent"/>: When price changes (non-no-op).</item>
/// <item><see cref="ProductDescribedDomainEvent"/>: When description is updated.</item>
/// <item><see cref="ProductDiscontinuedDomainEvent"/>: When transitioning Active → Discontinued.</item>
/// <item><see cref="ProductReactivatedDomainEvent"/>: When transitioning Discontinued → Active (admin only).</item>
/// </list>
/// </remarks>
public sealed class Product : AggregateRoot<Guid>, IAuditableEntity
{
    public Sku Sku { get; private set; } = null!;
    public ProductName Name { get; private set; } = null!;
    public ProductDescription Description { get; private set; } = null!;
    public Guid CategoryId { get; private set; }
    public BrandName Brand { get; private set; } = null!;
    public Money Price { get; private set; } = null!;
    public ProductStatus Status { get; private set; } = null!;
    public Dimensions? Dimensions { get; private set; }

    private readonly List<ImageReference> _images = [];
    public IReadOnlyCollection<ImageReference> Images => _images;

    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset LastModifiedUtc { get; private set; }

    private Product()
    {
    }

    /// <summary>
    /// Creates a new product in <see cref="ProductStatus.Active"/> status.
    /// SKU uniqueness is an application-layer concern (pre-checked against the read side);
    /// this factory validates only value-composition rules.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="ProductCreatedDomainEvent"/> with <c>OccurredOnUtc = utcNow</c> on success.
    /// </remarks>
    public static Result<Product> Create(
        Sku sku,
        ProductName name,
        ProductDescription description,
        Guid categoryId,
        BrandName brand,
        Money price,
        Dimensions? dimensions,
        IReadOnlyCollection<ImageReference> images,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(brand);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(images);

        if (categoryId == Guid.Empty)
        {
            return Result.Fail(ProductErrors.CategoryIdRequired());
        }

        // Catalog-local invariant I-1: Price.Amount > 0. Money is a signed quantity, so this
        // rule lives in the aggregate rather than in Money.Create.
        if (price.Amount <= 0)
        {
            return Result.Fail(ProductErrors.PriceMustBePositive());
        }

        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Sku = sku,
            Name = name,
            Description = description,
            CategoryId = categoryId,
            Brand = brand,
            Price = price,
            Status = ProductStatus.Active,
            Dimensions = dimensions
        };

        product._images.AddRange(images.OrderBy(i => i.DisplayOrder));

        product.AddDomainEvent(new ProductCreatedDomainEvent
        {
            ProductId = product.Id,
            Sku = sku,
            Name = name,
            CategoryId = categoryId,
            Price = price,
            OccurredOnUtc = utcNow
        });

        return product;
    }

    /// <summary>
    /// Updates the product's price. No-op when the new price equals the current price.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="ProductPriceChangedDomainEvent"/> with <c>OccurredOnUtc = utcNow</c>
    /// on a non-no-op change.
    /// </remarks>
    public Result UpdatePrice(Money newPrice, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(newPrice);

        // Catalog-local invariant I-1: Price.Amount > 0 (Money is permissive on sign).
        if (newPrice.Amount <= 0)
        {
            return Result.Fail(ProductErrors.PriceMustBePositive());
        }

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Fail(ProductErrors.CannotRepriceDiscontinued());
        }

        // ADR-0002: a product's price is single-currency for its lifetime. A reprice changes the
        // amount, never the currency — a currency change is rejected rather than silently emitting a
        // ProductPriceChangedEvent whose single Currency field would mislabel the old-price amount.
        if (newPrice.Currency != Price.Currency)
        {
            return Result.Fail(
                ProductErrors.CannotChangePriceCurrency(Price.Currency.Name, newPrice.Currency.Name));
        }

        if (newPrice == Price)
        {
            return Result.Ok();
        }

        var oldPrice = Price;
        Price = newPrice;

        AddDomainEvent(new ProductPriceChangedDomainEvent
        {
            ProductId = Id,
            OldPrice = oldPrice,
            NewPrice = newPrice,
            OccurredOnUtc = utcNow
        });

        return Result.Ok();
    }

    /// <summary>
    /// Overwrites the product description.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="ProductDescribedDomainEvent"/> with <c>OccurredOnUtc = utcNow</c>
    /// on success.
    /// </remarks>
    public Result Describe(ProductDescription newDescription, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(newDescription);

        if (Status == ProductStatus.Discontinued)
        {
            return Result.Fail(ProductErrors.CannotModifyDiscontinued());
        }

        Description = newDescription;

        AddDomainEvent(new ProductDescribedDomainEvent
        {
            ProductId = Id,
            NewDescription = newDescription,
            OccurredOnUtc = utcNow
        });

        return Result.Ok();
    }

    /// <summary>
    /// Transitions the product from <see cref="ProductStatus.Active"/> to
    /// <see cref="ProductStatus.Discontinued"/>. Requires a non-empty <paramref name="reason"/>.
    /// Throws <see cref="DataIntegrityException"/> when the current status is not Active
    /// (the UI must gate the button).
    /// </summary>
    /// <remarks>
    /// Raises <see cref="ProductDiscontinuedDomainEvent"/> with <c>OccurredOnUtc = utcNow</c>
    /// on success.
    /// </remarks>
    public Result Discontinue(string reason, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Fail(ProductErrors.ReasonRequired());
        }

        if (!Status.CanTransitionTo(ProductStatus.Discontinued))
        {
            return Result.Fail(ProductErrors.CannotDiscontinueInStatus(Status.Name));
        }

        Status = ProductStatus.Discontinued;

        AddDomainEvent(new ProductDiscontinuedDomainEvent
        {
            ProductId = Id,
            Reason = reason,
            OccurredOnUtc = utcNow
        });

        return Result.Ok();
    }

    /// <summary>
    /// Transitions the product from <see cref="ProductStatus.Discontinued"/> back to
    /// <see cref="ProductStatus.Active"/>. Requires <paramref name="adminReactivation"/>
    /// to be <c>true</c> (policy error otherwise). Throws <see cref="DataIntegrityException"/>
    /// when <paramref name="adminReactivation"/> is <c>true</c> but the current status is
    /// not Discontinued (UI bug).
    /// </summary>
    /// <remarks>
    /// Raises <see cref="ProductReactivatedDomainEvent"/> with <c>OccurredOnUtc = utcNow</c>
    /// on success. Not published as an external Kafka event in v1.
    /// </remarks>
    public Result Reactivate(bool adminReactivation, DateTimeOffset utcNow)
    {
        if (!adminReactivation)
        {
            return Result.Fail(ProductErrors.ReactivationRequiresAdminFlag());
        }

        if (Status != ProductStatus.Discontinued)
        {
            return Result.Fail(ProductErrors.CannotReactivateInStatus(Status.Name));
        }

        Status = ProductStatus.Active;

        AddDomainEvent(new ProductReactivatedDomainEvent
        {
            ProductId = Id,
            OccurredOnUtc = utcNow
        });

        return Result.Ok();
    }
}
