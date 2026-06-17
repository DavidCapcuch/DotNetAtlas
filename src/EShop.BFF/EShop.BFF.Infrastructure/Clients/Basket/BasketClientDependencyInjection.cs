using EShop.BFF.Infrastructure.Clients.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Auth;

namespace EShop.BFF.Infrastructure.Clients.Basket;

internal static class BasketClientDependencyInjection
{
    /// <summary>
    /// Registers the Basket typed client: bound + validated options, an <c>HttpClient</c> at the configured
    /// base URL, an <b>RFC 8693 token-exchange</b> bearer on the <c>basket.read</c> scope (ADR-0010 amendment
    /// 2026-06-06 — re-audiences the user token to <c>basket-service</c> while preserving the buyer
    /// <c>sub</c>), and the shared resilience pipeline (bff.md § 2.1). The exchange handler is registered
    /// before the resilience handler so a retried request keeps its exchanged Authorization header
    /// (bff.md § 2.3). Requires <c>services.AddUserTokenExchange()</c> + <c>AddServiceAuth("bff")</c> at the
    /// composition root.
    /// </summary>
    public static IServiceCollection AddBasketClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptionsWithValidateOnStart<BasketClientOptions>()
            .Bind(configuration.GetSection(BasketClientOptions.Section))
            .ValidateDataAnnotations();

        // AddUserTokenExchange pins the scope at DI-build time; read it from config (default basket.read).
        var scope = configuration.GetSection(BasketClientOptions.Section)[nameof(UpstreamClientOptions.Scope)]
            ?? BasketClientOptions.DefaultScope;

        services
            .AddHttpClient<IBasketClient, BasketHttpClient>((serviceProvider, http) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<BasketClientOptions>>().Value;
                http.BaseAddress = UpstreamBaseAddress.From(options.BaseUrl);

                // The resilience pipeline owns per-attempt + total timeouts (bff.md § 2.1).
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddUserTokenExchange(scope)
            .AddBffResilience("basket");

        return services;
    }
}
