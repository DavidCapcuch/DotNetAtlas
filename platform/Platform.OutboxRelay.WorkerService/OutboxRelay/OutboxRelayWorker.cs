using Microsoft.Extensions.Options;
using Platform.OutboxRelay.WorkerService.Observability.Metrics;
using Platform.OutboxRelay.WorkerService.OutboxRelay.Config;

namespace Platform.OutboxRelay.WorkerService.OutboxRelay;

/// <summary>
/// Background service that periodically polls the outbox table and publishes messages to Kafka.
/// Implements graceful shutdown with configurable flush and total shutdown timeouts.
/// </summary>
public sealed class OutboxRelayWorker : BackgroundService
{
    private readonly ILogger<OutboxRelayWorker> _logger;
    private readonly OutboxRelayOptions _outboxRelayOptions;
    private readonly OutboxRelayMetrics _outboxRelayMetrics;
    private readonly OutboxMessageRelay _outboxMessageRelay;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxRelayWorker"/> class.
    /// </summary>
    /// <param name="options">Configuration options for polling interval, batch size, and topic mappings.</param>
    /// <param name="logger">Logger for recording worker events and errors.</param>
    /// <param name="outboxRelayMetrics">Metrics collector for tracking the last successful execution.</param>
    /// <param name="outboxMessageRelay">Outbox Message Relay for publishing outbox messages.</param>
    public OutboxRelayWorker(
        IOptions<OutboxRelayOptions> options,
        ILogger<OutboxRelayWorker> logger,
        OutboxRelayMetrics outboxRelayMetrics,
        OutboxMessageRelay outboxMessageRelay)
    {
        _outboxRelayOptions = options.Value;
        _logger = logger;
        _outboxRelayMetrics = outboxRelayMetrics;
        _outboxMessageRelay = outboxMessageRelay;
    }

    /// <summary>
    /// Executes the outbox relay polling loop until cancellation is requested.
    /// </summary>
    /// <param name="stoppingToken">Token to signal graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OutboxRelay started. Config: polling={PollingMs}ms, batch={BatchSize}, " +
            "flushTimeout={FlushMs}ms, shutdownTimeout={ShutdownMs}ms",
            _outboxRelayOptions.PollingIntervalMs, _outboxRelayOptions.BatchSize,
            _outboxRelayOptions.FlushTimeoutMs, _outboxRelayOptions.ShutdownTimeoutMs);

        using var periodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_outboxRelayOptions.PollingIntervalMs));

        while (await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var wasSuccessful = await _outboxMessageRelay.PublishOutboxMessagesAsync(stoppingToken);
                if (wasSuccessful)
                {
                    _outboxRelayMetrics.RecordSuccessfulExecution();
                }
            }
            catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation(ex, "Shutdown requested during message processing");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox batch");
            }
        }

        _logger.LogInformation("OutboxRelay polling stopped, beginning shutdown sequence");
    }

    /// <summary>
    /// Graceful shutdown - flushes pending messages to ensure no data loss during shutdown.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OutboxRelay graceful shutdown initiated");

        var flushSuccessful = _outboxMessageRelay.FlushProducer(cancellationToken);
        if (!flushSuccessful)
        {
            _logger.LogWarning("Kafka producer flush failed during graceful shutdown");
        }

        await base.StopAsync(cancellationToken);
    }
}
