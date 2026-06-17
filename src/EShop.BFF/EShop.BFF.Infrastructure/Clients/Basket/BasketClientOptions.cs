using EShop.BFF.Infrastructure.Clients.Common;

namespace EShop.BFF.Infrastructure.Clients.Basket;

/// <summary>
/// Bound from <c>Bff:Basket</c>. Default scope <c>basket.read</c> — the RFC 8693 exchange re-audiences the
/// user token to <c>basket-service</c> via this scope while preserving the buyer <c>sub</c> (ADR-0010).
/// </summary>
internal sealed class BasketClientOptions : UpstreamClientOptions
{
    public const string Section = "Bff:Basket";

    public const string DefaultScope = "basket.read";
}
