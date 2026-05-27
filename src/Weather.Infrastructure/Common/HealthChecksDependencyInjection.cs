using Confluent.Kafka;
using HealthChecks.ApplicationStatus.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.ServiceDefaults.Config;
using StackExchange.Redis;
using Weather.Infrastructure.Common.Authentication;
using Weather.Infrastructure.Common.Config;
using Weather.Infrastructure.Common.Constants;
using Weather.Infrastructure.HttpClients.WeatherProviders.OpenMeteo;
using Weather.Infrastructure.HttpClients.WeatherProviders.WeatherApiCom;
using Weather.Infrastructure.Messaging.Kafka.Config;
using Weather.Infrastructure.Persistence.Database;

namespace Weather.Infrastructure.Common;

/// <summary>
/// Health-check surface for the Weather demo — Self,
/// <see cref="WeatherDbContext"/>, Redis (idempotency cache), the WeatherApi.com
/// and Open-Meteo external provider URLs, the Keycloak IDM discovery endpoint,
/// Hangfire (degraded + unhealthy thresholds), and Kafka. The <c>HealthCheckTags.ApiTag</c>
/// tag is Weather-specific and used for upstream provider classification — the 7 BC
/// services do not depend on external URL groups and therefore do not carry it.
/// Per-probe timeouts come from <see cref="HealthChecksOptions"/>; the
/// <c>AddDbContextCheck</c> EF Core extension does not expose a direct timeout
/// parameter.
/// </summary>
internal static class HealthChecksDependencyInjection
{
    internal static IServiceCollection AddWeatherHealthChecks(
        this IServiceCollection services,
        bool isDeployedEnvironment,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<HealthChecksOptions>()
            .BindConfiguration(HealthChecksOptions.Section)
            .ValidateDataAnnotations();

        var timeouts = configuration
            .GetRequiredSection(HealthChecksOptions.Section)
            .Get<HealthChecksOptions>()!;

        var openMeteoOptions = configuration
            .GetRequiredSection(OpenMeteoOptions.Section)
            .Get<OpenMeteoOptions>()!;
        var weatherApiComOptions = configuration
            .GetRequiredSection(WeatherApiComOptions.Section)
            .Get<WeatherApiComOptions>()!;
        var idmAuthorityUrl = configuration[$"{AuthConfigSections.OAuthConfigSection}:Authority"]!;
        if (!idmAuthorityUrl.EndsWith('/'))
        {
            idmAuthorityUrl += "/";
        }

        var kafkaOptions = configuration
            .GetRequiredSection(KafkaOptions.Section)
            .Get<KafkaOptions>()!;
        var producerConfig = new ProducerConfig { BootstrapServers = kafkaOptions.BrokersFlat };

        services.AddHealthChecks()
            .AddApplicationStatus(
                "Self",
                tags: [ServiceDefaultHealthCheckTags.LivenessTag, ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.SelfTimeout)
            .AddDbContextCheck<WeatherDbContext>(
                name: "Weather DB",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy)
            .AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                name: "Redis",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.RedisTimeout)
            .AddUrlGroup(
                new Uri(weatherApiComOptions.BaseUrl), weatherApiComOptions.BaseUrl,
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.ApiTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.ExternalProvidersApiTimeout)
            .AddUrlGroup(
                new Uri($"{openMeteoOptions.GeoBaseUrl}v1/search?name=Berlin&count=1"), openMeteoOptions.GeoBaseUrl,
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.ApiTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.ExternalProvidersApiTimeout)
            .AddUrlGroup(
                new Uri($"{openMeteoOptions.BaseUrl}v1/forecast"), openMeteoOptions.BaseUrl,
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.ApiTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.ExternalProvidersApiTimeout)
            .AddOpenIdConnectServer(
                oidcSvrUri: new Uri(idmAuthorityUrl),
                discoverConfigurationSegment: ".well-known/openid-configuration",
                name: "Keycloak IDM",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag, HealthCheckTags.ApiTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.IdmApiTimeout)
            .AddHangfire(options => options.MaximumJobsFailed = timeouts.Hangfire.DegradedMaximumJobsFailed,
                "Hangfire Degraded Check",
                failureStatus: HealthStatus.Degraded,
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.Hangfire.Timeout)
            .AddHangfire(options =>
                {
                    options.MaximumJobsFailed = timeouts.Hangfire.UnhealthyMaximumJobsFailed;
                    options.MinimumAvailableServers = timeouts.Hangfire.MinimumAvailableServers;
                }, "Hangfire Unhealthy Check",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                timeout: timeouts.Hangfire.Timeout)
            .AddKafka(
                producerConfig,
                name: "Kafka",
                tags: [ServiceDefaultHealthCheckTags.ReadinessTag],
                failureStatus: HealthStatus.Unhealthy,
                timeout: timeouts.KafkaTimeout);

        if (!isDeployedEnvironment)
        {
            services.AddHealthChecksUI(settings =>
            {
                settings.SetEvaluationTimeInSeconds(5);
                settings.AddHealthCheckEndpoint("Liveness", ServiceDefaultHealthCheckTags.HealthEndpointPath);
                settings.AddHealthCheckEndpoint("Readiness", ServiceDefaultHealthCheckTags.ReadinessEndpointPath);
                settings.SetNotifyUnHealthyOneTimeUntilChange();
            })
            .AddInMemoryStorage();
        }

        return services;
    }
}
