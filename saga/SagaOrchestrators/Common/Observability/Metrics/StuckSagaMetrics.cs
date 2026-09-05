using System.Diagnostics.Metrics;

// ReSharper disable NotAccessedField.Local -> observable metrics

namespace SagaOrchestrators.Common.Observability.Metrics;

/// <summary>
/// The orchestrator's current stuck-saga count, published as a gauge and read by
/// <c>StuckSagaHealthCheck</c>. <see cref="StuckSagaMetricsCollector"/> is the only writer.
/// </summary>
public sealed class StuckSagaMetrics
{
    private readonly ObservableGauge<int> _stuckPaymentSagasGauge;
    private volatile int _stuckPaymentSagaCount;

    public StuckSagaMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ApplicationInfo.AppName);

        _stuckPaymentSagasGauge = meter.CreateObservableGauge(
            "saga.stuck.payment",
            observeValue: () => _stuckPaymentSagaCount,
            unit: "count",
            description: "Number of stuck payment sagas");
    }

    /// <summary>
    /// Zero until the first sweep completes, so a reader sees "none known" rather than "none".
    /// </summary>
    public int StuckPaymentSagaCount => _stuckPaymentSagaCount;

    public void SetStuckPaymentSagaCount(int count) => _stuckPaymentSagaCount = count;
}
