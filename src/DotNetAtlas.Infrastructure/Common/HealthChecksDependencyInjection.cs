using Confluent.Kafka;
using DotNetAtlas.Infrastructure.Common.Authentication;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.OpenMeteo;
using DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.WeatherApiCom;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;
using DotNetAtlas.Infrastructure.Persistence.Database;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using StackExchange.Redis;

namespace DotNetAtlas.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for health checks infrastructure.
/// Configures health checks for database, messaging, APIs, and external services.
/// </summary>
public static class HealthChecksDependencyInjection
{
    /// <summary>
    /// Configures health checks for the application.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration manager.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddHealthChecksInternal(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<HealthChecksOptions>()
            .BindConfiguration(HealthChecksOptions.Section)
            .ValidateDataAnnotations();

        var timeoutsOptions = configuration
            .GetRequiredSection(HealthChecksOptions.Section)
            .Get<HealthChecksOptions>()!;

        var openMeteoOptions = configuration
            .GetRequiredSection(OpenMeteoOptions.Section)
            .Get<OpenMeteoOptions>()!;
        var weatherApiComOptions = configuration
            .GetRequiredSection(WeatherApiComOptions.Section)
            .Get<WeatherApiComOptions>()!;
        var fusionAuthUrl = configuration[$"{AuthConfigSections.OAuthConfigSection}:Authority"]!;

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = kafkaOptions.BrokersFlat
        };

        services.AddHealthChecks()
            .AddCheck("Self", () => HealthCheckResult.Healthy(),
                tags: [InfrastructureConstants.LivenessTag, InfrastructureConstants.ReadinessTag],
                timeout: timeoutsOptions.SelfTimeout)
            .AddDbContextCheck<WeatherDbContext>(
                name: "Weather DB",
                tags: [InfrastructureConstants.ReadinessTag, InfrastructureConstants.DatabaseTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                tags: [InfrastructureConstants.ReadinessTag, InfrastructureConstants.DatabaseTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.RedisTimeout,
                name: "Redis")
            .AddUrlGroup(
                new Uri(weatherApiComOptions.BaseUrl), weatherApiComOptions.BaseUrl,
                tags:
                [
                    InfrastructureConstants.ReadinessTag, InfrastructureConstants.ApiTag
                ],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.ExternalProvidersApiTimeout)
            .AddUrlGroup(
                new Uri($"{openMeteoOptions.GeoBaseUrl}v1/search?name=Berlin&count=1"), openMeteoOptions.GeoBaseUrl,
                tags:
                [
                    InfrastructureConstants.ReadinessTag, InfrastructureConstants.ApiTag
                ],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.ExternalProvidersApiTimeout)
            .AddUrlGroup(
                new Uri($"{openMeteoOptions.BaseUrl}v1/forecast"), openMeteoOptions.BaseUrl,
                tags:
                [
                    InfrastructureConstants.ReadinessTag, InfrastructureConstants.ApiTag
                ],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.ExternalProvidersApiTimeout)
            .AddOpenIdConnectServer(
                oidcSvrUri: new Uri(fusionAuthUrl),
                discoverConfigurationSegment: "/.well-known/openid-configuration",
                name: "FusionAuth IDM",
                tags: [InfrastructureConstants.ReadinessTag, InfrastructureConstants.ApiTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.IdmApiTimeout)
            .AddHangfire(options => options.MaximumJobsFailed = timeoutsOptions.Hangfire.DegradedMaximumJobsFailed,
                "Hangfire Degraded Check",
                failureStatus: HealthStatus.Degraded,
                tags: [InfrastructureConstants.ReadinessTag],
                timeout: timeoutsOptions.Hangfire.Timeout)
            .AddHangfire(options =>
                {
                    options.MaximumJobsFailed = timeoutsOptions.Hangfire.UnhealthyMaximumJobsFailed;
                    options.MinimumAvailableServers = timeoutsOptions.Hangfire.MinimumAvailableServers;
                }, "Hangfire Unhealthy Check",
                failureStatus: HealthStatus.Unhealthy,
                tags: [InfrastructureConstants.ReadinessTag],
                timeout: timeoutsOptions.Hangfire.Timeout)
            .AddKafka(producerConfig, "healthchecks", "Kafka",
                tags: [InfrastructureConstants.ReadinessTag, InfrastructureConstants.MessagingTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.KafkaTimeout);

        services.AddHealthChecksUI(settings =>
            {
                settings.SetEvaluationTimeInSeconds(5);
                settings.AddHealthCheckEndpoint("Liveness", InfrastructureConstants.HealthEndpointPath);
                settings.AddHealthCheckEndpoint("Readiness", InfrastructureConstants.ReadinessEndpointPath);
                settings.SetNotifyUnHealthyOneTimeUntilChange();
            })
            .AddSqlServerStorage(configuration.GetConnectionString(nameof(ConnectionStringsOptions.Weather))!);

        return services;
    }

    /// <summary>
    /// Configures Prometheus health check metrics exporter.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
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
