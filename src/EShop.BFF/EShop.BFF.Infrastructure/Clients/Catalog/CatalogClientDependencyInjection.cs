using EShop.BFF.Infrastructure.Clients.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Auth;

namespace EShop.BFF.Infrastructure.Clients.Catalog;

internal static class CatalogClientDependencyInjection
{
    /// <summary>
    /// Registers the Catalog typed client: bound + validated options, an <c>HttpClient</c> pointed at
    /// the configured base URL, a <c>client_credentials</c> service token on the <c>catalog.read</c>
    /// scope (ADR-0010), and the shared resilience pipeline (bff.md § 2.1). The auth handler is
    /// registered before the resilience handler so retried requests keep their Authorization header
    /// (bff.md § 2.3).
    /// </summary>
    public static IServiceCollection AddCatalogClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptionsWithValidateOnStart<CatalogClientOptions>()
            .Bind(configuration.GetSection(CatalogClientOptions.Section))
            .ValidateDataAnnotations();

        // AddServiceAuth pins the scope at DI-build time; read it from config (default catalog.read).
        var scope = configuration.GetSection(CatalogClientOptions.Section)[nameof(UpstreamClientOptions.Scope)]
            ?? CatalogClientOptions.DefaultScope;

        services
            .AddHttpClient<ICatalogClient, CatalogHttpClient>((serviceProvider, http) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<CatalogClientOptions>>().Value;
                http.BaseAddress = UpstreamBaseAddress.From(options.BaseUrl);

                // The resilience pipeline owns per-attempt + total timeouts (bff.md § 2.1);
                // disable HttpClient's own timeout so it can't pre-empt them.
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddServiceAuth(scope)
            .AddBffResilience("catalog");

        return services;
    }
}
