using System.Diagnostics.Metrics;

// ReSharper disable NotAccessedField.Local -> observable metrics

namespace DotNetAtlas.OutboxRelay.WorkerService.Observability.Metrics;

/// <summary>
/// Custom metrics for OutboxRelay service monitoring.
/// </summary>
public sealed class OutboxRelayMetrics
{
    private readonly Counter<long> _messagesPublishedCounter;
    private readonly Counter<long> _messagesFailedCounter;
    private readonly Histogram<double> _processingDurationHistogram;
    private readonly ObservableGauge<long> _outboxSizeGauge;
    private readonly ObservableGauge<long> _outboxHeadLagGauge;
    private readonly ObservableGauge<long> _outboxTailLagGauge;
    private readonly ObservableGauge<double> _timeSinceLastExecutionGauge;
    private long _currentOutboxPendingMessages;
    private long _currentOutboxHeadLagMs;
    private long _currentOutboxTailLagMs;
    public DateTimeOffset LastSuccessfulExecution { get; private set; } = DateTimeOffset.MinValue;

    public OutboxRelayMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(OutboxRelayInstrumentation.AppName);

        _messagesPublishedCounter = meter.CreateCounter<long>(
            "outbox_messages_published_total",
            description: "Total number of outbox messages successfully published to Kafka",
            unit: "{messages}");

        _messagesFailedCounter = meter.CreateCounter<long>(
            "outbox_messages_failed_total",
            description: "Total number of outbox messages that failed to publish",
            unit: "{messages}");

        _processingDurationHistogram = meter.CreateHistogram<double>(
            "outbox_processing_duration_seconds",
            description: "Time taken to process outbox message batches",
            unit: "s");

        _outboxSizeGauge = meter.CreateObservableGauge(
            "outbox_size_messages",
            description: "Current size of outbox table (number of pending messages)",
            unit: "{messages}",
            observeValue: () => _currentOutboxPendingMessages);

        _outboxHeadLagGauge = meter.CreateObservableGauge(
            "outbox_head_lag_milliseconds",
            description: "Lag of newest outbox message (head of queue) in milliseconds",
            unit: "ms",
            observeValue: () => _currentOutboxHeadLagMs);

        _outboxTailLagGauge = meter.CreateObservableGauge(
            "outbox_tail_lag_milliseconds",
            description: "Lag of oldest outbox message (tail of queue) in milliseconds",
            unit: "ms",
            observeValue: () => _currentOutboxTailLagMs);

        _timeSinceLastExecutionGauge = meter.CreateObservableGauge(
            "outbox_time_since_last_successful_execution_seconds",
            description: "Time since last successful outbox processing execution",
            unit: "s",
            observeValue: () =>
                LastSuccessfulExecution == DateTimeOffset.MinValue
                    ? double.NaN
                    : (DateTimeOffset.UtcNow - LastSuccessfulExecution).TotalSeconds);
    }

    public void RecordMessagesPublished(int count, string messageType)
    {
        _messagesPublishedCounter.Add(count,
            new KeyValuePair<string, object?>(OutboxRelayMetricConstants.Tags.MessageType, messageType));
    }

    public void RecordMessagesFailed(int count, string messageType)
    {
        _messagesFailedCounter.Add(count,
            new KeyValuePair<string, object?>(OutboxRelayMetricConstants.Tags.MessageType, messageType));
    }

    public void RecordSuccessfulExecution() => LastSuccessfulExecution = DateTimeOffset.UtcNow;

    public void RecordProcessingDuration(TimeSpan duration, int processedCount)
    {
        if (processedCount > 0)
        {
            _processingDurationHistogram.Record(
                duration.TotalSeconds,
                new KeyValuePair<string, object?>(
                    OutboxRelayMetricConstants.Tags.ProcessedCount,
                    GetProcessedCountCategory(processedCount)));
        }
    }

    public void SetOutboxPendingMessages(long size) => _currentOutboxPendingMessages = size;
    public void SetOutboxHeadLagMs(long lagMs) => _currentOutboxHeadLagMs = lagMs;
    public void SetOutboxTailLagMs(long lagMs) => _currentOutboxTailLagMs = lagMs;

    private static string GetProcessedCountCategory(int processedCount)
    {
        return processedCount switch
        {
            0 => OutboxRelayMetricConstants.ProcessedCountValues.None,
            1 => OutboxRelayMetricConstants.ProcessedCountValues.Single,
            <= 5 => OutboxRelayMetricConstants.ProcessedCountValues.Tiny,
            <= 10 => OutboxRelayMetricConstants.ProcessedCountValues.Small,
            <= 25 => OutboxRelayMetricConstants.ProcessedCountValues.Medium,
            <= 50 => OutboxRelayMetricConstants.ProcessedCountValues.Large,
            <= 100 => OutboxRelayMetricConstants.ProcessedCountValues.XLarge,
            <= 250 => OutboxRelayMetricConstants.ProcessedCountValues.XxLarge,
            <= 500 => OutboxRelayMetricConstants.ProcessedCountValues.Huge,
            <= 1000 => OutboxRelayMetricConstants.ProcessedCountValues.Massive,
            _ => OutboxRelayMetricConstants.ProcessedCountValues.Gigantic
        };
    }
}
