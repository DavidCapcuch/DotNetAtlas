using Catalog.Products;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Infrastructure.Messaging;

/// <summary>
/// Inbound Kafka adapter for Catalog's product lifecycle events on <c>catalog.products</c> — a new,
/// re-priced, or discontinued product may change the home page's featured set, so each removes the
/// <c>home-page</c> tag (bff.md § 2.2 / § 3.4).
/// </summary>
internal sealed class ProductEventCacheInvalidator
    : CacheInvalidatorBase,
        IMessageHandler<ProductCreatedEvent>,
        IMessageHandler<ProductPriceChangedEvent>,
        IMessageHandler<ProductDiscontinuedEvent>
{
    public ProductEventCacheInvalidator(IFusionCache cache, ILogger<ProductEventCacheInvalidator> logger)
        : base(cache, logger)
    {
    }

    public Task Handle(IMessageContext context, ProductCreatedEvent message) => InvalidateAsync(message, context);

    public Task Handle(IMessageContext context, ProductPriceChangedEvent message) => InvalidateAsync(message, context);

    public Task Handle(IMessageContext context, ProductDiscontinuedEvent message) => InvalidateAsync(message, context);
}
