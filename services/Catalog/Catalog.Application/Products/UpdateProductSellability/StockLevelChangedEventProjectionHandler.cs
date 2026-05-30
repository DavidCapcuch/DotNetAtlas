using Catalog.Application.Common.Data;
using Catalog.Domain.Products.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.Application.Products.UpdateProductSellability;

/// <summary>
/// Owns the <c>product_search_view.IsSellable</c> projection update driven by Inventory's
/// <c>StockLevelChangedEvent</c> events. Inbound Kafka adapters in Catalog.Infrastructure are thin
/// translators — the projection write lives here in Application so architecture-tests.md § 2.1
/// ("projection writes only in *ProjectionHandler") holds across the cross-BC inbox path too,
/// not just the in-process domain-event path (CAT-ARCH-C02 / #174).
/// </summary>
public sealed class StockLevelChangedEventProjectionHandler : IStockLevelChangedEventProjector
{
    private readonly ICatalogDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StockLevelChangedEventProjectionHandler> _logger;

    public StockLevelChangedEventProjectionHandler(
        ICatalogDbContext db,
        TimeProvider timeProvider,
        ILogger<StockLevelChangedEventProjectionHandler> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleAsync(Guid productId, int newAvailable, CancellationToken ct)
    {
        var row = await _db.ProductSearchView
            .FirstOrDefaultAsync(r => r.ProductId == productId, ct);
        if (row is null)
        {
            // Graceful degradation: Inventory may publish for a product Catalog hasn't projected
            // yet (the cross-BC ordering is event-driven and eventually consistent). When Catalog
            // later creates the row via ProductCreatedDomainEvent, IsSellable defaults from the
            // aggregate status and will be corrected the next time stock crosses a threshold.
            _logger.LogInformation(
                "StockLevelChangedEvent for unknown ProductId {ProductId}; "
                + "Catalog has not yet projected this product. Skipping.",
                productId);
            return;
        }

        var isActive = row.Status == ProductStatus.Active.Name;
        var newIsSellable = isActive && newAvailable > 0;

        if (row.IsSellable == newIsSellable)
        {
            return;
        }

        row.IsSellable = newIsSellable;
        // Manual bump: ProductSearchViewRow is a projection, not an IAuditableEntity
        // (the UpdateAuditableEntitiesInterceptor doesn't fire on projection rows).
        row.LastUpdatedAtUtc = _timeProvider.GetUtcNow();

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Updated IsSellable={IsSellable} on ProductSearchView row for {ProductId} "
            + "(Status={Status}, NewAvailable={NewAvailable})",
            newIsSellable, productId, row.Status, newAvailable);
    }
}
