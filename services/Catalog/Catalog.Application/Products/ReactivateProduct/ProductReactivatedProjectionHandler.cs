using Catalog.Application.Common.Data;
using Catalog.Domain.Products.Events;
using Catalog.Domain.Products.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Application.Products.ReactivateProduct;

public sealed class ProductReactivatedProjectionHandler
    : IDomainEventHandler<ProductReactivatedDomainEvent>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<ProductReactivatedProjectionHandler> _logger;

    public ProductReactivatedProjectionHandler(
        ICatalogDbContext db,
        ILogger<ProductReactivatedProjectionHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(ProductReactivatedDomainEvent domainEvent, CancellationToken ct)
    {
        var row = await _db.ProductSearchView.FindAsync([domainEvent.ProductId], ct);
        if (row is null)
        {
            _logger.LogWarning(
                "Received ProductReactivatedDomainEvent for {ProductId} but no projection row exists; skipping.",
                domainEvent.ProductId);
            return;
        }

        row.Status = ProductStatus.Active.Name;

        // IsSellable mirrors Product.Status.IsSellable at this point; the cross-BC stock input
        // (via StockLevelChanged from Inventory) arrives in M4 and may later gate this to false.
        row.IsSellable = ProductStatus.Active.IsSellable;
        row.LastUpdatedAtUtc = domainEvent.OccurredOnUtc;
    }
}
