using Basket.Application.Abstractions;
using Basket.Infrastructure.Common.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Auth;

namespace Basket.Infrastructure.ExternalServices.Catalog;

/// <summary>
/// DI wiring for the Catalog Anti-Corruption Layer — binds
/// <see cref="CatalogServiceOptions"/> and registers
/// <see cref="ProductCatalogHttpAdapter"/> as a typed <see cref="HttpClient"/>
/// behind <see cref="IProductCatalogQueryPort"/>. The HttpClient carries OAuth2
/// bearer tokens for the configured scope (ADR-0010); W3C trace context
/// propagates automatically via OpenTelemetry's HttpClient instrumentation.
/// </summary>
/// <remarks>
/// Requires <c>services.AddServiceAuth("basket-service")</c> to be registered
/// on the same collection — it provides the delegating handler that this
/// extension's <c>IHttpClientBuilder.AddServiceAuth(scope)</c> attaches to the
/// typed client. Basket registers it in <c>AddBasketAuthentication</c>
/// (ADR-0010), wired from <c>Program.cs</c>.
/// </remarks>
public static class CatalogClientDependencyInjection
{
    public static IServiceCollection AddBasketCatalogClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptionsWithValidateOnStart<CatalogServiceOptions>()
            .Bind(configuration.GetSection(CatalogServiceOptions.Section))
            .ValidateDataAnnotations();

        // Scope is read from configuration up-front because
        // IHttpClientBuilder.AddServiceAuth(scope) takes a string at DI-build
        // time, not a factory. Defaults to "catalog.read" per ADR-0010; ops
        // can override without a code change.
        var scope = configuration
            .GetSection(CatalogServiceOptions.Section)[nameof(CatalogServiceOptions.Scope)]
            ?? "catalog.read";

        services
            .AddHttpClient<IProductCatalogQueryPort, ProductCatalogHttpAdapter>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<CatalogServiceOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
            })
            .AddServiceAuth(scope);

        return services;
    }
}
