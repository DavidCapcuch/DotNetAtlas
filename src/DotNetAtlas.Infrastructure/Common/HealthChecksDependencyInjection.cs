using Confluent.Kafka;
using DotNetAtlas.Infrastructure.Common.Authentication;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Common.Constants;
using DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.OpenMeteo;
using DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.WeatherApiCom;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;
using DotNetAtlas.Infrastructure.Persistence.Database;
using DotNetAtlas.ServiceDefaults.Config;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
            .AddApplicationStatus("Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeoutsOptions.SelfTimeout)
            .AddDbContextCheck<WeatherDbContext>(
                name: "Weather DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.DatabaseTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.DatabaseTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.RedisTimeout,
                name: "Redis")
            .AddUrlGroup(
                new Uri(weatherApiComOptions.BaseUrl), weatherApiComOptions.BaseUrl,
                tags:
                [
                    ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.ApiTag
                ],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.ExternalProvidersApiTimeout)
            .AddUrlGroup(
                new Uri($"{openMeteoOptions.GeoBaseUrl}v1/search?name=Berlin&count=1"), openMeteoOptions.GeoBaseUrl,
                tags:
                [
                    ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.ApiTag
                ],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.ExternalProvidersApiTimeout)
            .AddUrlGroup(
                new Uri($"{openMeteoOptions.BaseUrl}v1/forecast"), openMeteoOptions.BaseUrl,
                tags:
                [
                    ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.ApiTag
                ],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.ExternalProvidersApiTimeout)
            .AddOpenIdConnectServer(
                oidcSvrUri: new Uri(fusionAuthUrl),
                discoverConfigurationSegment: "/.well-known/openid-configuration",
                name: "FusionAuth IDM",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.ApiTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.IdmApiTimeout)
            .AddHangfire(options => options.MaximumJobsFailed = timeoutsOptions.Hangfire.DegradedMaximumJobsFailed,
                "Hangfire Degraded Check",
                failureStatus: HealthStatus.Degraded,
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeoutsOptions.Hangfire.Timeout)
            .AddHangfire(options =>
                {
                    options.MaximumJobsFailed = timeoutsOptions.Hangfire.UnhealthyMaximumJobsFailed;
                    options.MinimumAvailableServers = timeoutsOptions.Hangfire.MinimumAvailableServers;
                }, "Hangfire Unhealthy Check",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeoutsOptions.Hangfire.Timeout)
            .AddKafka(producerConfig, "healthchecks", "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.MessagingTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeoutsOptions.KafkaTimeout);

        services.AddHealthChecksUI(settings =>
            {
                settings.SetEvaluationTimeInSeconds(5);
                settings.AddHealthCheckEndpoint("Liveness", ServiceDefaultHealthCheckTags.HealthEndpointPath);
                settings.AddHealthCheckEndpoint("Readiness", ServiceDefaultHealthCheckTags.ReadinessEndpointPath);
                settings.SetNotifyUnHealthyOneTimeUntilChange();
            })
            .AddSqlServerStorage(configuration.GetConnectionString(nameof(ConnectionStringsOptions.Weather))!);

        return services;
    }
}
