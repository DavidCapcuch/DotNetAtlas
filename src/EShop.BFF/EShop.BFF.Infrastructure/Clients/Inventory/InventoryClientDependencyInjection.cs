using EShop.BFF.Infrastructure.Clients.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Auth;

namespace EShop.BFF.Infrastructure.Clients.Inventory;

internal static class InventoryClientDependencyInjection
{
    /// <summary>
    /// Registers the Inventory typed client: bound + validated options, an <c>HttpClient</c> pointed
    /// at the configured base URL, a <c>client_credentials</c> service token on the
    /// <c>inventory.read</c> scope (ADR-0010), and the shared resilience pipeline (bff.md § 2.1).
    /// </summary>
    /// <remarks>
    /// Inventory's stock-level endpoint is currently <c>AllowAnonymous</c> (ADR-0034: availability is
    /// public), so the attached <c>inventory.read</c> token is a no-op at the callee today — kept for
    /// spec fidelity (ADR-0010) and forward-compatibility should the read ever be scope-gated.
    /// </remarks>
    public static IServiceCollection AddInventoryClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptionsWithValidateOnStart<InventoryClientOptions>()
            .Bind(configuration.GetSection(InventoryClientOptions.Section))
            .ValidateDataAnnotations();

        var scope = configuration.GetSection(InventoryClientOptions.Section)[nameof(UpstreamClientOptions.Scope)]
            ?? InventoryClientOptions.DefaultScope;

        services
            .AddHttpClient<IInventoryClient, InventoryHttpClient>((serviceProvider, http) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<InventoryClientOptions>>().Value;
                http.BaseAddress = UpstreamBaseAddress.From(options.BaseUrl);
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddServiceAuth(scope)
            .AddBffResilience("inventory");

        return services;
    }
}
