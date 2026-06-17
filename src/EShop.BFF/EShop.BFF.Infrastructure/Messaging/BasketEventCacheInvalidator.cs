using Basket.Sessions;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Infrastructure.Messaging;

/// <summary>
/// Inbound Kafka adapter for Basket's <c>BasketCheckoutInitiatedEvent</c> on <c>basket.sessions</c> — the
/// basket has been converted to an order, so it removes the buyer's <c>basket-bff-{UserId}</c> tag to wipe
/// the BFF's cached basket promptly (bff.md § 2.2 / § 3.2). Defense-in-depth: the BFF also invalidates this
/// tag synchronously on the checkout it fronts (bff.md § 3.5, a later slice); this consumer covers
/// out-of-band / cross-instance checkout transitions.
/// </summary>
internal sealed class BasketEventCacheInvalidator
    : CacheInvalidatorBase, IMessageHandler<BasketCheckoutInitiatedEvent>
{
    public BasketEventCacheInvalidator(IFusionCache cache, ILogger<BasketEventCacheInvalidator> logger)
        : base(cache, logger)
    {
    }

    public Task Handle(IMessageContext context, BasketCheckoutInitiatedEvent message) =>
        InvalidateAsync(message, context);
}
