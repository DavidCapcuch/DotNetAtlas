using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Observability.Metrics;

namespace SagaOrchestrators.Common.Observability.HealthChecks;

/// <summary>
/// Reports the stuck-saga backlog that <see cref="StuckSagaMetricsCollector"/> swept, capped at
/// <see cref="HealthStatus.Degraded"/> — which the readiness endpoint still serves as 200, so the
/// backlog raises the alarm without pulling any instance out of rotation.
/// <para>
/// It reads a value rather than computing one, so it performs no I/O and needs no timeout. That
/// makes the reported count stale by up to one sweep interval and zero before the first sweep;
/// both are correct for a warning signal, and a database the sweep cannot reach is the
/// <c>Saga DB</c> check's to report.
/// </para>
/// </summary>
public sealed class StuckSagaHealthCheck : IHealthCheck
{
    private readonly StuckSagaMetrics _stuckSagaMetrics;
    private readonly StuckSagaOptions _stuckSagaOptions;

    public StuckSagaHealthCheck(StuckSagaMetrics stuckSagaMetrics, IOptions<StuckSagaOptions> options)
    {
        _stuckSagaMetrics = stuckSagaMetrics;
        _stuckSagaOptions = options.Value;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stuckPaymentSagaCount = _stuckSagaMetrics.StuckPaymentSagaCount;

        if (stuckPaymentSagaCount < _stuckSagaOptions.MaxStuckSagasBeforeDegraded)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                $"{stuckPaymentSagaCount} stuck payment sagas"));
        }

        return Task.FromResult(HealthCheckResult.Degraded(
            $"Found {stuckPaymentSagaCount} stuck payment sagas",
            data: new Dictionary<string, object>
            {
                ["StuckPaymentSagaCount"] = stuckPaymentSagaCount,
                ["ThresholdMinutes"] = _stuckSagaOptions.StuckSagaThresholdMinutes,
                ["MaxDegraded"] = _stuckSagaOptions.MaxStuckSagasBeforeDegraded
            }));
    }
}
