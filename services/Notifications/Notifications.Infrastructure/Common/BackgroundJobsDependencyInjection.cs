using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Notifications.Infrastructure.Common.Config;

namespace Notifications.Infrastructure.Common;

/// <summary>
/// Hangfire wiring for the per-channel dispatch jobs (ADR-0032). Storage lives in the
/// <c>notifications</c> Postgres DB (Hangfire's own <c>hangfire</c> schema). The processing
/// server is gated off in the test host — integration
/// tests invoke the channel dispatchers directly (dispatcher-direct seam), so no job runner is needed.
/// </summary>
internal static class BackgroundJobsDependencyInjection
{
    internal static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services,
        bool enableProcessingServer)
    {
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
            config.UsePostgreSqlStorage(
                c => c.UseNpgsqlConnection(connectionStrings.Notifications),
                new PostgreSqlStorageOptions
                {
                    JobExpirationCheckInterval =
                        TimeSpan.FromMilliseconds(hangfireOptions.JobExpirationCheckIntervalMs),
                    QueuePollInterval = TimeSpan.FromMilliseconds(hangfireOptions.QueuePollIntervalMs),
                });
        });

        if (enableProcessingServer)
        {
            services.AddHangfireServer((sp, options) =>
            {
                var hangfireOptions = sp.GetRequiredService<IOptions<HangfireOptions>>().Value;
                options.SchedulePollingInterval =
                    TimeSpan.FromMilliseconds(hangfireOptions.SchedulePollingIntervalMs);
                options.CancellationCheckInterval =
                    TimeSpan.FromMilliseconds(hangfireOptions.CancellationCheckIntervalMs);
                options.Queues = hangfireOptions.Queues;
            });
        }

        return services;
    }
}
