using Catalog.Categories;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Infrastructure.Messaging;

/// <summary>
/// Inbound Kafka adapter for Catalog's category lifecycle events on <c>catalog.categories</c> — a new
/// category changes the home page's category tree, so it removes the <c>home-page</c> tag (bff.md § 2.2).
/// </summary>
internal sealed class CategoryEventCacheInvalidator
    : CacheInvalidatorBase, IMessageHandler<CategoryCreatedEvent>
{
    public CategoryEventCacheInvalidator(IFusionCache cache, ILogger<CategoryEventCacheInvalidator> logger)
        : base(cache, logger)
    {
    }

    public Task Handle(IMessageContext context, CategoryCreatedEvent message) => InvalidateAsync(message, context);
}
