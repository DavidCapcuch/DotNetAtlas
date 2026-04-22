using Catalog.Application.Common.Data;
using Catalog.Domain.Products.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Application.Products.DescribeProduct;

public sealed class ProductDescribedProjectionHandler
    : IDomainEventHandler<ProductDescribedDomainEvent>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<ProductDescribedProjectionHandler> _logger;

    public ProductDescribedProjectionHandler(
        ICatalogDbContext db,
        ILogger<ProductDescribedProjectionHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(ProductDescribedDomainEvent domainEvent, CancellationToken ct)
    {
        var row = await _db.ProductSearchView.FindAsync([domainEvent.ProductId], ct);
        if (row is null)
        {
            _logger.LogWarning(
                "Received ProductDescribedDomainEvent for {ProductId} but no projection row exists; skipping.",
                domainEvent.ProductId);
            return;
        }

        row.Description = domainEvent.NewDescription.Value;
        row.LastUpdatedAtUtc = domainEvent.OccurredOnUtc;
    }
}
