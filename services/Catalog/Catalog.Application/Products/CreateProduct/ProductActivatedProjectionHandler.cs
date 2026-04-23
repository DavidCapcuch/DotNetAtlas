using Catalog.Application.Common.Data;
using Catalog.Domain.Products.Events;
using Catalog.Domain.Products.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Application.Products.CreateProduct;

/// <summary>
/// Projection handler for <see cref="ProductActivatedDomainEvent"/>. Flips the
/// <c>product_search_view</c> row from <c>Draft</c> → <c>Active</c> and sets
/// <c>IsSellable = true</c>. M3 does not ship an HTTP/Kafka command path that invokes
/// <see cref="Catalog.Domain.Products.Product.Activate"/> — this handler exists so the
/// aggregate-event → projection contract stays complete when the command lands in a later
/// milestone.
/// </summary>
/// <remarks>
/// Lives in the CreateProduct feature folder because Activate is the natural next step in
/// the creation flow. If a dedicated ActivateProduct command folder appears later, move it.
/// </remarks>
public sealed class ProductActivatedProjectionHandler
    : IDomainEventHandler<ProductActivatedDomainEvent>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<ProductActivatedProjectionHandler> _logger;

    public ProductActivatedProjectionHandler(
        ICatalogDbContext db,
        ILogger<ProductActivatedProjectionHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(ProductActivatedDomainEvent domainEvent, CancellationToken ct)
    {
        var row = await _db.ProductSearchView.FindAsync([domainEvent.ProductId], ct);
        if (row is null)
        {
            _logger.LogWarning(
                "Received ProductActivatedDomainEvent for {ProductId} but no projection row exists; skipping.",
                domainEvent.ProductId);
            return;
        }

        row.Status = ProductStatus.Active.Name;
        row.IsSellable = ProductStatus.Active.IsSellable;
        row.LastUpdatedAtUtc = domainEvent.OccurredOnUtc;
    }
}
