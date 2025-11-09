using Confluent.Kafka;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay.Config;
using Microsoft.Extensions.Options;
using Serilog;

namespace DotNetAtlas.OutboxRelay.WorkerService.Common;

public static class OutboxRelayDependencyInjection
{
    /// <summary>
    /// Configures Kafka producer and outbox processing services.
    /// </summary>
    /// <param name="builder">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static WebApplicationBuilder AddOutboxRelayWorker(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptionsWithValidateOnStart<OutboxRelayOptions>()
            .BindConfiguration(OutboxRelayOptions.Section)
            .ValidateDataAnnotations();

        builder.Services.AddOptionsWithValidateOnStart<KafkaProducerOptions>()
            .BindConfiguration(KafkaProducerOptions.Section)
            .ValidateDataAnnotations();

        var outboxRelayConfig = builder.Configuration
            .GetSection(OutboxRelayOptions.Section)
            .Get<OutboxRelayOptions>();

        if (outboxRelayConfig != null)
        {
            var hostShutdownTimeout = TimeSpan.FromMilliseconds(
                outboxRelayConfig.ShutdownTimeoutMs);

            builder.Services.Configure<HostOptions>(options =>
            {
                options.ShutdownTimeout = hostShutdownTimeout;
            });

            Log.Information(
                "Host shutdown timeout configured to {TimeoutSeconds}s",
                hostShutdownTimeout.TotalSeconds);
        }

        builder.Services.AddHostedService<OutboxRelayWorker>();
        builder.Services.AddSingleton<OutboxMessageRelay>();

        builder.Services.AddSingleton<IProducer<string?, byte[]>>(sp =>
        {
            var producerOptions = sp.GetRequiredService<IOptions<KafkaProducerOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<Program>>();

            return new ProducerBuilder<string?, byte[]>(producerOptions)
                .SetLogHandler((producer, logMessage) =>
                {
                    var level = logMessage.Level switch
                    {
                        SyslogLevel.Emergency or SyslogLevel.Alert or SyslogLevel.Critical => LogLevel.Critical,
                        SyslogLevel.Error => LogLevel.Error,
                        SyslogLevel.Warning => LogLevel.Warning,
                        SyslogLevel.Notice or SyslogLevel.Info => LogLevel.Information,
                        _ => LogLevel.Debug
                    };
                    logger.Log(level, "{ProducerName}: {Message}", producer.Name, logMessage.Message);
                })
                .Build();
        });

        return builder;
    }
}
