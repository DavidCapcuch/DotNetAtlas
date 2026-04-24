using Basket.Application.Abstractions;
using Basket.Infrastructure.Common.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Auth;
using Platform.ServiceDefaults.CorrelationId;

namespace Basket.Infrastructure.ExternalServices.Catalog;

/// <summary>
/// DI wiring for the Catalog Anti-Corruption Layer — binds
/// <see cref="CatalogServiceOptions"/> and registers
/// <see cref="ProductCatalogHttpAdapter"/> as a typed <see cref="HttpClient"/>
/// behind <see cref="IProductCatalogQueryPort"/>. The HttpClient carries
/// correlation-id headers (ADR-0008) and OAuth2 bearer tokens for the
/// configured scope (ADR-0010).
/// </summary>
/// <remarks>
/// Caller MUST have invoked <c>services.AddServiceAuth("basket")</c> and
/// <c>services.AddCorrelationId()</c> before calling this extension — those
/// register the delegating handlers that
/// <c>IHttpClientBuilder.AddServiceAuth(scope)</c> and
/// <c>IHttpClientBuilder.AddCorrelationIdPropagation()</c> attach. Those
/// service-collection-level registrations land in <c>Program.cs</c> at
/// milestone M6; this extension is intentionally not yet wired from the host.
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
            .AddCorrelationIdPropagation()
            .AddServiceAuth(scope);

        return services;
    }
}
