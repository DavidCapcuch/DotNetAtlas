namespace Catalog.Application.Common.ReadModels;

/// <summary>
/// Denormalized row for the <c>catalog.product_search_view</c> projection described in
/// <c>docs/bc-design/catalog.md § 9</c>. Populated and updated atomically with the write model
/// by projection handlers (<see cref="Platform.SharedKernel.Base.DomainEvents.IDomainEventHandler{T}"/>).
/// EF Core mapping is owned by <c>Catalog.Infrastructure</c>; this POCO only defines the
/// shape the Application layer reads from and writes to.
/// </summary>
public sealed class ProductSearchViewRow
{
    /// <summary>Primary key mirroring <c>Product.Id</c>.</summary>
    public Guid ProductId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Guid CategoryId { get; set; }

    /// <summary>Materialized category path (e.g., <c>/electronics/computers/laptops</c>).</summary>
    public string CategoryPath { get; set; } = string.Empty;

    /// <summary>Human-readable breadcrumb (e.g., <c>Electronics &gt; Computers &gt; Laptops</c>).</summary>
    public string CategoryBreadcrumb { get; set; } = string.Empty;

    public string BrandName { get; set; } = string.Empty;

    public decimal PriceAmount { get; set; }

    public string PriceCurrency { get; set; } = string.Empty;

    /// <summary>Lifecycle status as the SmartEnum <c>Name</c> (Draft|Active|Discontinued).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Serialized <c>Dimensions</c> value object, or <c>null</c> for digital products.</summary>
    public string? DimensionsJson { get; set; }

    /// <summary>Serialized ordered list of <c>ImageReference</c> value objects.</summary>
    public string ImagesJson { get; set; } = "[]";

    /// <summary>
    /// True iff the product is sellable right now. Derived from <c>Status == Active</c> plus
    /// stock level from Inventory (via the <c>StockLevelChanged</c> consumer).
    /// </summary>
    public bool IsSellable { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset LastUpdatedAtUtc { get; set; }

    /// <summary>
    /// Correlation-id of the originating HTTP request (ADR-0008). Populated from
    /// <c>HttpContext.Items[CorrelationIdContextKeys.HttpContextItemsKey]</c> in the API
    /// layer, or <see cref="Guid.Empty"/> when no HTTP pipeline is in play.
    /// </summary>
    public Guid CorrelationId { get; set; }
}
