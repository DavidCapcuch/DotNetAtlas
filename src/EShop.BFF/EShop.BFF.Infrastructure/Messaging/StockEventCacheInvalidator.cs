using Inventory.Stock;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Infrastructure.Messaging;

/// <summary>
/// Inbound Kafka adapter for Inventory's <c>StockLevelChangedEvent</c> on <c>inventory.stock-events</c> —
/// an availability threshold crossing may change the home page's stock overlay / highlights, so it removes
/// the <c>home-page</c> tag (bff.md § 2.2 / § 3.4).
/// </summary>
internal sealed class StockEventCacheInvalidator
    : CacheInvalidatorBase, IMessageHandler<StockLevelChangedEvent>
{
    public StockEventCacheInvalidator(IFusionCache cache, ILogger<StockEventCacheInvalidator> logger)
        : base(cache, logger)
    {
    }

    public Task Handle(IMessageContext context, StockLevelChangedEvent message) => InvalidateAsync(message, context);
}
