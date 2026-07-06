using EShop.BFF.Infrastructure.Clients.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Auth;

namespace EShop.BFF.Infrastructure.Clients.Basket;

internal static class BasketWriteClientDependencyInjection
{
    /// <summary>
    /// Registers the Basket <b>write</b> typed client (bff.md § 3.6): the same <c>Bff:Basket</c> base URL and
    /// resilience pipeline as the read client, but the RFC 8693 exchange on the <c>basket.write</c> scope (see
    /// <see cref="BasketClientOptions.WriteScope"/> for why it is a separate client). The exchange handler is
    /// registered before the resilience handler so a retried mutation keeps its exchanged Authorization header
    /// (bff.md § 2.3). Requires <c>services.AddUserTokenExchange()</c> + <c>AddServiceAuth("bff")</c> at the
    /// composition root.
    /// </summary>
    public static IServiceCollection AddBasketWriteClient(this IServiceCollection services)
    {
        // Reuses the read client's bound + validated BasketClientOptions (same Bff:Basket section → same
        // base URL); AddBasketClient binds it. The write scope is a pinned invariant, not config-bound.
        services
            .AddHttpClient<IBasketWriteClient, BasketWriteHttpClient>((serviceProvider, http) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<BasketClientOptions>>().Value;
                http.BaseAddress = UpstreamBaseAddress.From(options.BaseUrl);

                // The resilience pipeline owns per-attempt + total timeouts (bff.md § 2.1).
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddUserTokenExchange(BasketClientOptions.WriteScope)
            .AddBffResilience("basket");

        return services;
    }
}
