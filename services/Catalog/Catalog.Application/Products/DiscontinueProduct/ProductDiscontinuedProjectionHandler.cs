using Catalog.Application.Common.Data;
using Catalog.Domain.Products.Events;
using Catalog.Domain.Products.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Application.Products.DiscontinueProduct;

public sealed class ProductDiscontinuedProjectionHandler
    : IDomainEventHandler<ProductDiscontinuedDomainEvent>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<ProductDiscontinuedProjectionHandler> _logger;

    public ProductDiscontinuedProjectionHandler(
        ICatalogDbContext db,
        ILogger<ProductDiscontinuedProjectionHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(ProductDiscontinuedDomainEvent domainEvent, CancellationToken ct)
    {
        var row = await _db.ProductSearchView.FindAsync([domainEvent.ProductId], ct);
        if (row is null)
        {
            _logger.LogWarning(
                "Received ProductDiscontinuedDomainEvent for {ProductId} but no projection row exists; skipping.",
                domainEvent.ProductId);
            return;
        }

        row.Status = ProductStatus.Discontinued.Name;
        row.IsSellable = false;
        row.LastUpdatedAtUtc = domainEvent.OccurredOnUtc;
    }
}
