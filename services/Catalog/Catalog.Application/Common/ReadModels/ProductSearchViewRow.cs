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

    /// <summary>Lifecycle status as the SmartEnum <c>Name</c> (Active|Discontinued).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// The optional <c>Dimensions</c> value object, flattened to scalars exactly as the write model
    /// stores it. All four are populated together or all are null (digital/service products) — a
    /// table <c>CHECK</c> enforces it, so a partial row cannot be written.
    /// </summary>
    public decimal? DimensionsLength { get; set; }

    /// <inheritdoc cref="DimensionsLength"/>
    public decimal? DimensionsWidth { get; set; }

    /// <inheritdoc cref="DimensionsLength"/>
    public decimal? DimensionsHeight { get; set; }

    /// <inheritdoc cref="DimensionsLength"/>
    public string? DimensionsUnit { get; set; }

    /// <summary>Serialized ordered list of <c>ImageReference</c> value objects.</summary>
    public string ImagesJson { get; set; } = "[]";

    /// <summary>
    /// True iff the product is sellable right now. Derived from <c>Status == Active</c> plus
    /// stock level from Inventory (via the <c>StockLevelChangedEvent</c> consumer).
    /// </summary>
    public bool IsSellable { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset LastUpdatedAtUtc { get; set; }
}
