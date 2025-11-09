using DotNetAtlas.OutboxRelay.WorkerService.Common.Config;
using DotNetAtlas.OutboxRelay.WorkerService.Observability.HealthChecks;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay.Config;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;

namespace DotNetAtlas.OutboxRelay.WorkerService.Common;

/// <summary>
/// Dependency injection extensions for health checks infrastructure.
/// Configures health checks for database, messaging, and service execution monitoring.
/// </summary>
public static class HealthChecksDependencyInjection
{
    /// <summary>
    /// Configures health checks for the OutboxRelay worker.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHealthChecksInternal(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<OutboxRelayHealthCheckOptions>()
            .BindConfiguration(OutboxRelayHealthCheckOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<HealthChecksOptions>()
            .BindConfiguration(HealthChecksOptions.Section)
            .ValidateDataAnnotations();

        var timeoutsOptions = configuration
            .GetRequiredSection(HealthChecksOptions.Section)
            .Get<HealthChecksOptions>()!;

        var kafkaProducerOptions = configuration
            .GetRequiredSection(KafkaProducerOptions.Section)
            .Get<KafkaProducerOptions>()!;

        services.AddHealthChecks()
            .AddCheck("Self", () => HealthCheckResult.Healthy(),
                tags: [InfrastructureConstants.LivenessTag, InfrastructureConstants.ReadinessTag],
                timeout: timeoutsOptions.SelfTimeout)
            .AddDbContextCheck<OutboxDbContext>(
                name: "Outbox DbContext",
                tags:
                [
                    InfrastructureConstants.ReadinessTag, InfrastructureConstants.DatabaseTag
                ],
                failureStatus: HealthStatus.Unhealthy)
            .AddKafka(kafkaProducerOptions, "healthchecks", "Kafka",
                tags:
                [
                    InfrastructureConstants.ReadinessTag, InfrastructureConstants.MessagingTag
                ],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.KafkaTimeout)
            .AddCheck<OutboxRelayHealthCheck>(
                name: "OutboxRelay Execution",
                tags: [InfrastructureConstants.LivenessTag, InfrastructureConstants.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.OutboxRelayExecutionTimeout);
        services.AddSingleton<OutboxRelayHealthCheck>();

        return services;
    }

    /// <summary>
    /// Maps health check endpoints with appropriate filters.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapHealthChecksInternal(this WebApplication app)
    {
        app.MapHealthChecks(InfrastructureConstants.ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(InfrastructureConstants.ReadinessTag)
        }).ShortCircuit();

        app.MapHealthChecks(InfrastructureConstants.HealthEndpointPath, new HealthCheckOptions
        {
            Predicate = healthCheck => healthCheck.Tags.Contains(InfrastructureConstants.LivenessTag)
        }).ShortCircuit();

        return app;
    }

    public static WebApplication UseHealthChecksPrometheusExporterInternal(this WebApplication app)
    {
        // Suppress default prometheus-net collectors and collect only health-related metrics to avoid duplicated scraping.
        // As of now, there is no standardized way to push health metrics through OTEL Collector
        // all other collected metrics are unaffected and still exported through OTEL Collector to prometheus.
        Metrics.SuppressDefaultMetrics();

        app.UseHealthChecksPrometheusExporter(InfrastructureConstants.PrometheusEndpointPath, options =>
        {
            options.Predicate = healthCheck => healthCheck.Tags.Contains(InfrastructureConstants.ReadinessTag);
            options.ResultStatusCodes = new Dictionary<HealthStatus, int>
            {
                // Prometheus expects 200 also for degraded state, otherwise throws in the scrape job
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status200OK
            };
        });

        return app;
    }
}
