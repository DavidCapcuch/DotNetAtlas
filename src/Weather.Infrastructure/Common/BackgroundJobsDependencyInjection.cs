using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Weather.Application.WeatherAlerts.Common.Abstractions;
using Weather.Infrastructure.BackgroundJobs;
using Weather.Infrastructure.BackgroundJobs.Config;
using Weather.Infrastructure.Common.Config;

namespace Weather.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for background jobs.
/// </summary>
internal static class BackgroundJobsDependencyInjection
{
    /// <summary>
    /// Configures Hangfire background job processing.
    /// Sets up SQL Server storage and job server.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services)
    {
        services.AddOptionsWithValidateOnStart<FakeWeatherDataGeneratorBackgroundJobOptions>()
            .BindConfiguration(FakeWeatherDataGeneratorBackgroundJobOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<ExpiredSubscriptionsJobOptions>()
            .BindConfiguration(ExpiredSubscriptionsJobOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<HangfireOptions>()
            .BindConfiguration(HangfireOptions.Section)
            .ValidateDataAnnotations();

        services.AddHangfire((sp, config) =>
        {
            var hangfireOptions = sp.GetRequiredService<IOptions<HangfireOptions>>().Value;
            var connectionStrings = sp.GetRequiredService<IOptions<ConnectionStringsOptions>>().Value;
            config.UseRecommendedSerializerSettings();
            config.UseSimpleAssemblyNameTypeSerializer();
            config.UseSerilogLogProvider();
            config.UseSqlServerStorage(connectionStrings.Weather,
                new SqlServerStorageOptions
                {
                    JobExpirationCheckInterval =
                        TimeSpan.FromMilliseconds(hangfireOptions.JobExpirationCheckIntervalMs),
                    QueuePollInterval = TimeSpan.FromMilliseconds(hangfireOptions.QueuePollIntervalMs)
                });
        });

        services.AddHangfireServer((sp, options) =>
        {
            var hangfireOptions = sp.GetRequiredService<IOptions<HangfireOptions>>().Value;
            options.SchedulePollingInterval = TimeSpan.FromMilliseconds(hangfireOptions.SchedulePollingIntervalMs);
            options.CancellationCheckInterval = TimeSpan.FromMilliseconds(hangfireOptions.CancellationCheckIntervalMs);
            options.Queues = hangfireOptions.Queues;
        });

        services.AddScoped<IFakeWeatherDataGenerationJobScheduler, FakeWeatherDataGenerationJobScheduler>();

        return services;
    }
}
