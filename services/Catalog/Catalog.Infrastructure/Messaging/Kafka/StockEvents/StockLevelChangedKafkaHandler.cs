using Catalog.Application.Common.Data;
using Catalog.Domain.Products.ValueObjects;
using Inventory.Stock;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.Infrastructure.Messaging.Kafka.StockEvents;

/// <summary>
/// Inbound consumer for <see cref="StockLevelChanged"/> events from Inventory.
/// Updates <c>product_search_view.IsSellable</c> using the formula
/// <c>Status == "Active" AND NewAvailable &gt; 0</c> so BFF callers see a single
/// boolean instead of having to join Inventory's stream.
/// </summary>
/// <remarks>
/// <para>
/// Inbox-dedup middleware (<c>Platform.KafkaFlow.Inbox.EFCore</c>) runs in front of
/// this handler — the same MessageId arriving twice is processed exactly once.
/// </para>
/// <para>
/// Graceful degradation when Catalog hasn't seen the product yet: a
/// <c>StockLevelChanged</c> for an unknown <c>ProductId</c> is logged at
/// <c>Information</c> level and skipped. Inventory may publish for a product that
/// was created on its side first (the cross-BC ordering is event-driven and
/// eventually consistent). When Catalog later creates the row via
/// <c>ProductCreatedDomainEvent</c>, <c>IsSellable</c> defaults to <c>false</c> and
/// will be corrected the next time stock crosses a threshold.
/// </para>
/// </remarks>
internal sealed class StockLevelChangedKafkaHandler : IMessageHandler<StockLevelChanged>
{
    // CAT-RV-H02 (Wave-1 closeout): combine WorkerStopped with a per-message budget so a
    // slow Postgres query during a Kafka rebalance can't hold the partition until the
    // worker stops — misbehaving messages then starve other partitions.
    internal static readonly TimeSpan PerMessageBudget = TimeSpan.FromSeconds(30);

    private readonly ICatalogDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StockLevelChangedKafkaHandler> _logger;

    public StockLevelChangedKafkaHandler(
        ICatalogDbContext db,
        TimeProvider timeProvider,
        ILogger<StockLevelChangedKafkaHandler> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, StockLevelChanged message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        using var perMessageCts = CancellationTokenSource.CreateLinkedTokenSource(
            context.ConsumerContext.WorkerStopped);
        perMessageCts.CancelAfter(PerMessageBudget);
        var ct = perMessageCts.Token;

        var row = await _db.ProductSearchView
            .FirstOrDefaultAsync(r => r.ProductId == message.ProductId, ct);
        if (row is null)
        {
            _logger.LogInformation(
                "StockLevelChanged for unknown ProductId {ProductId}; "
                + "Catalog has not yet projected this product. Skipping.",
                message.ProductId);
            return;
        }

        var isActive = row.Status == ProductStatus.Active.Name;
        var newIsSellable = isActive && message.NewAvailable > 0;

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
            newIsSellable, message.ProductId, row.Status, message.NewAvailable);
    }
}
