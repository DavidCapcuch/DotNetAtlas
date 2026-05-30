using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Infrastructure.Common;

/// <summary>
/// Composition root for the Catalog Infrastructure layer. Called from
/// <c>Catalog.Api.Program.cs</c> after <c>AddServiceDefaults</c> (correlation-id +
/// service-auth per ADR-0008/0010) and <c>AddApplication</c> (validators, CQRS
/// handlers, projection handlers, outbox publishers).
/// </summary>
/// <remarks>
/// Chains the infrastructure slices:
/// <list type="bullet">
/// <item><description>
/// <see cref="PersistenceDependencyInjection.AddDatabase"/> — <see cref="Persistence.Database.CatalogDbContext"/>
/// bound to Postgres with snake_case + exception-processor + the <c>DispatchDomainEventsInterceptor</c>
/// that fires the in-process projection write atomically with the aggregate save.
/// </description></item>
/// <item><description>
/// <see cref="MessagingDependencyInjection.AddKafkaMessaging"/> — KafkaFlow cluster
/// with the <c>StockLevelChangedEvent</c> inbox consumer, transactional outbox + DLT producer,
/// and correlation-id propagation middleware.
/// </description></item>
/// <item><description>
/// <see cref="ObservabilityDependencyInjection.AddOpenTelemetry"/> — OTel tracing +
/// metrics with ASP.NET Core, HTTP, EF Core, and Redis instrumentation. Gated on
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>; ADR-0011 PII redaction processor is wired here.
/// </description></item>
/// <item><description>
/// <see cref="HealthChecksDependencyInjection.AddCatalogHealthChecks"/> — readiness
/// probes (Self + Postgres + Kafka + redis-cache + Schema Registry) tagged so
/// <c>MapPlatformHealthCheckEndpoints</c> publishes them under <c>/api/readiness</c>.
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
            .AddDatabase(configuration, isDeployedEnvironment)
            .AddKafkaMessaging(configuration)
            .AddOpenTelemetry(isDeployedEnvironment, configuration)
            .AddCatalogHealthChecks(configuration);

        return services;
    }
}
