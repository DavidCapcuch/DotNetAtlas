using Basket.Infrastructure.ExternalServices.Catalog;
using Basket.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basket.Infrastructure.Common;

/// <summary>
/// Composition root for the Basket Infrastructure layer. Called from
/// <c>Basket.Api.Program.cs</c> after <c>AddServiceDefaults</c> (which
/// registers correlation-id + service-auth per ADR-0008/0010) and
/// <c>AddApplication</c> (which registers validators, CQRS handlers, and
/// domain-event dispatch).
/// </summary>
/// <remarks>
/// <para>
/// Chains four infrastructure slices:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>AddBasketRedisPersistence</c> — the Redis primary store: keyed
/// <c>IConnectionMultiplexer</c> for <c>redis-basket</c>, FusionCache
/// <c>"basket"</c>, and <c>IBasketRepository</c>. Distinct from
/// <see cref="PersistenceDependencyInjection.AddDatabase"/> below (the SQL
/// side-car) so the two persistence paths are unambiguous at the call site.
/// </description></item>
/// <item><description>
/// <c>AddDatabase</c> — the SQL side-car: <see cref="Persistence.Database.BasketDbContext"/>
/// bound to Postgres with snake_case + exception-processor, and the
/// <c>IBasketDbContext</c> application port binding.
/// </description></item>
/// <item><description>
/// <c>AddMessaging</c> — the transactional outbox (publishing
/// <c>BasketCheckoutInitiatedEvent</c> on <c>basket.sessions</c> via
/// <c>outbox-relay-basket</c>) and the inbox adapter against
/// <see cref="Persistence.Database.BasketDbContext"/>.
/// </description></item>
/// <item><description>
/// <c>AddBasketCatalogClient</c> — the Catalog Anti-Corruption Layer: a
/// typed <see cref="HttpClient"/> fronting <c>IProductCatalogQueryPort</c>
/// with correlation-id + service-auth delegating handlers. Requires
/// <c>AddCorrelationId()</c> + <c>AddServiceAuth("basket")</c> to have been
/// called upstream by ServiceDefaults.
/// </description></item>
/// <item><description>
/// <c>AddBasketHealthChecks</c> — readiness/liveness probes for self,
/// <see cref="Persistence.Database.BasketDbContext"/>, <c>redis-basket</c>
/// (per ADR-0016), and Kafka.
/// </description></item>
/// </list>
/// </remarks>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isDeployedEnvironment)
    {
        services
            .AddBasketRedisPersistence(configuration)
            .AddDatabase(configuration, isDeployedEnvironment)
            .AddMessaging(configuration)
            .AddBasketCatalogClient(configuration)
            .AddBasketHealthChecks(configuration);

        return services;
    }
}
