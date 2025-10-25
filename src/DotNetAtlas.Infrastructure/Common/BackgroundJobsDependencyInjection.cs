using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Infrastructure.BackgroundJobs;
using DotNetAtlas.Infrastructure.BackgroundJobs.Config;
using DotNetAtlas.Infrastructure.Common.Config;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for background jobs.
/// </summary>
internal static class BackgroundJobsDependencyInjection
{
    /// <summary>
    /// Configures Hangfire background job processing.
    /// Sets up SQL Server storage, job server, and job schedulers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services)
    {
        services.AddOptionsWithValidateOnStart<FakeWeatherAlertJobOptions>()
            .BindConfiguration(FakeWeatherAlertJobOptions.Section)
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

        services.AddScoped<IWeatherAlertJobScheduler, FakeWeatherAlertJobScheduler>();

        return services;
    }
}
