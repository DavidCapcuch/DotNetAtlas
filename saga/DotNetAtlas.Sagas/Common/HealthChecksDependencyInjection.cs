using Confluent.Kafka;
using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Persistence.Database;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;

namespace DotNetAtlas.Sagas.Common;

/// <summary>
/// Extension methods for configuring health check endpoints.
/// </summary>
public static class HealthChecksDependencyInjection
{
    public static IServiceCollection AddHealthChecksInternal(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<HealthCheckTimeoutsOptions>()
            .BindConfiguration(HealthCheckTimeoutsOptions.Section)
            .ValidateDataAnnotations();

        var timeouts = configuration
            .GetSection(HealthCheckTimeoutsOptions.Section)
            .Get<HealthCheckTimeoutsOptions>() ?? new HealthCheckTimeoutsOptions();

        var sagaOptions = configuration
            .GetRequiredSection(SagaOptions.Section)
            .Get<SagaOptions>()!;

        var kafkaProducerConfig = new ProducerConfig
        {
            BootstrapServers = sagaOptions.KafkaBootstrapServers,
            ClientId = "saga-healthcheck"
        };

        services.AddHealthChecks()
            .AddApplicationStatus(
                name: "Self",
                tags: [InfrastructureConstants.LivenessTag, InfrastructureConstants.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<SubscriptionSagaDbContext>(
                name: "Saga Database",
                failureStatus: HealthStatus.Unhealthy,
                tags: [InfrastructureConstants.ReadinessTag, InfrastructureConstants.DatabaseTag])
            .AddCheck<SagaStateMachineHealthCheck>(
                name: "Saga StateMachine",
                failureStatus: HealthStatus.Degraded,
                tags: [InfrastructureConstants.ReadinessTag])
            .AddKafka(
                kafkaProducerConfig,
                topic: "healthchecks",
                name: "Kafka",
                tags: [InfrastructureConstants.ReadinessTag, InfrastructureConstants.MessagingTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.KafkaTimeout);

        return services;
    }

    /// <summary>
    /// Maps health check endpoints with appropriate filters.
    /// </summary>
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
