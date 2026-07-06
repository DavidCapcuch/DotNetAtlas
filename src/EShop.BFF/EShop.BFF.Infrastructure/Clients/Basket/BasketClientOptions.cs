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

    /// <summary>
    /// Exchange scope for the buyer-scoped <b>write</b> surface (bff.md § 3.6). Pinned here (not bound from
    /// config) — least-privilege keeps the write audience-scope a fixed invariant, distinct from the tunable
    /// read <see cref="UpstreamClientOptions.Scope"/>. The platform pins one exchange scope per
    /// <see cref="System.Net.Http.HttpClient"/>, so the write client is registered separately on this scope.
    /// </summary>
    public const string WriteScope = "basket.write";
}
