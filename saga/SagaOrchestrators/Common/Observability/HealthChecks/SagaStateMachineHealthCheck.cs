using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Persistence.Database;
using SagaOrchestrators.Payments.PaymentProcessingSaga;

namespace SagaOrchestrators.Common.Observability.HealthChecks;

/// <summary>
/// Health check for saga state machine infrastructure.
/// Verifies database connectivity and checks for stuck sagas.
/// Also exposes metrics per saga type for Grafana dashboards.
/// </summary>
#pragma warning disable SA1214 // Readonly fields should appear before non-readonly fields - backing fields must precede observable gauges
public sealed class SagaStateMachineHealthCheck : IHealthCheck
{
    // Instance readonly fields
    private readonly SagaDbContext _sagaDbContext;
    private readonly ILogger<SagaStateMachineHealthCheck> _logger;
    private readonly SagaHealthCheckOptions _sagaHealthCheckOptions;
    private readonly TimeProvider _timeProvider;

    // Static metrics infrastructure
    private static readonly Meter Meter = new(ApplicationInfo.AppName, ApplicationInfo.Version);

    // Backing fields for observable gauges - updated by health check, read by Prometheus
    private static int _stuckPaymentSagaCount;

    // Observable gauges for stuck saga counts per type - scraped by Prometheus/Grafana
    private static readonly ObservableGauge<int> StuckPaymentSagasGauge = Meter.CreateObservableGauge(
        "saga.stuck.payment",
        () => _stuckPaymentSagaCount,
        "count",
        "Number of stuck payment sagas");

    public SagaStateMachineHealthCheck(
        SagaDbContext sagaDbContext,
        ILogger<SagaStateMachineHealthCheck> logger,
        IOptions<SagaHealthCheckOptions> options,
        TimeProvider timeProvider)
    {
        _sagaDbContext = sagaDbContext;
        _logger = logger;
        _timeProvider = timeProvider;
        _sagaHealthCheckOptions = options.Value;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _sagaDbContext.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                return HealthCheckResult.Unhealthy("Cannot connect to saga database");
            }

            var stuckSagaThreshold = TimeSpan.FromMinutes(_sagaHealthCheckOptions.StuckSagaThresholdMinutes);
            var threshold = _timeProvider.GetUtcNow() - stuckSagaThreshold;

            var stuckPaymentCount = await _sagaDbContext.Set<PaymentProcessingSagaState>()
                .CountAsync(s =>
                        !PaymentProcessingSagaState.TerminalStates.Contains(s.CurrentState) &&
                        s.LastModifiedUtc < threshold,
                    cancellationToken);

            // Update metrics for Grafana/Prometheus scraping
            _stuckPaymentSagaCount = stuckPaymentCount;

            var stuckSagaCount = stuckPaymentCount;

            if (stuckSagaCount >= _sagaHealthCheckOptions.MaxStuckSagasBeforeUnhealthy)
            {
                _logger.LogError(
                    "Found {StuckSagaCount} stuck sagas ({StuckPaymentCount} payment) - no update in {ThresholdMinutes} minutes, exceeds unhealthy threshold of {MaxUnhealthy}",
                    stuckSagaCount,
                    stuckPaymentCount,
                    _sagaHealthCheckOptions.StuckSagaThresholdMinutes,
                    _sagaHealthCheckOptions.MaxStuckSagasBeforeUnhealthy);

                return HealthCheckResult.Unhealthy(
                    $"Found {stuckSagaCount} stuck sagas, exceeds maximum threshold",
                    data: new Dictionary<string, object>
                    {
                        ["StuckSagaCount"] = stuckSagaCount,
                        ["StuckPaymentSagaCount"] = stuckPaymentCount,
                        ["ThresholdMinutes"] = _sagaHealthCheckOptions.StuckSagaThresholdMinutes,
                        ["MaxUnhealthy"] = _sagaHealthCheckOptions.MaxStuckSagasBeforeUnhealthy
                    });
            }

            if (stuckSagaCount >= _sagaHealthCheckOptions.MaxStuckSagasBeforeDegraded)
            {
                _logger.LogWarning(
                    "Found {StuckSagaCount} potentially stuck sagas ({StuckPaymentCount} payment) - no update in {ThresholdMinutes} minutes",
                    stuckSagaCount,
                    stuckPaymentCount,
                    _sagaHealthCheckOptions.StuckSagaThresholdMinutes);

                return HealthCheckResult.Degraded(
                    $"Found {stuckSagaCount} potentially stuck sagas",
                    data: new Dictionary<string, object>
                    {
                        ["StuckSagaCount"] = stuckSagaCount,
                        ["StuckPaymentSagaCount"] = stuckPaymentCount,
                        ["ThresholdMinutes"] = _sagaHealthCheckOptions.StuckSagaThresholdMinutes,
                        ["MaxDegraded"] = _sagaHealthCheckOptions.MaxStuckSagasBeforeDegraded
                    });
            }

            return HealthCheckResult.Healthy("Saga state machine is healthy");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saga health check failed");
            return HealthCheckResult.Unhealthy("Saga health check failed", ex);
        }
    }
}
