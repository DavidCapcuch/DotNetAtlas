using Catalog.Application.Common.Data;
using Catalog.Domain.Products.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Application.Products.UpdateProductPrice;

public sealed class ProductPriceChangedProjectionDomainEventHandler
    : IDomainEventHandler<ProductPriceChangedDomainEvent>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<ProductPriceChangedProjectionDomainEventHandler> _logger;

    public ProductPriceChangedProjectionDomainEventHandler(
        ICatalogDbContext db,
        ILogger<ProductPriceChangedProjectionDomainEventHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(ProductPriceChangedDomainEvent domainEvent, CancellationToken ct)
    {
        var row = await _db.ProductSearchView.FindAsync([domainEvent.ProductId], ct);
        if (row is null)
        {
            _logger.LogWarning(
                "Received ProductPriceChangedDomainEvent for {ProductId} but no projection row exists; skipping.",
                domainEvent.ProductId);
            return;
        }

        row.PriceAmount = domainEvent.NewPrice.Amount;
        row.PriceCurrency = domainEvent.NewPrice.Currency.Name;
        row.LastUpdatedAtUtc = domainEvent.OccurredOnUtc;
    }
}
