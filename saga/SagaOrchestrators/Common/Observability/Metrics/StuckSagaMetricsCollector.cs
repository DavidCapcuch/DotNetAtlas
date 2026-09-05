using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Persistence.Database;
using SagaOrchestrators.Payments.PaymentProcessingSaga;

namespace SagaOrchestrators.Common.Observability.Metrics;

/// <summary>
/// Counts the payment sagas that have stopped progressing and publishes the result to
/// <see cref="StuckSagaMetrics"/>.
/// <para>
/// The count is swept here rather than computed inside the health check that reports it, because
/// a stuck-saga backlog is a fact about the system rather than about this instance: every replica
/// reads the same rows, so a probe answering the question would take them all out of rotation
/// together while none of them is broken. Off the probe path the DbContext's retrying execution
/// strategy becomes an asset — a sweep can afford to retry for as long as the strategy wants,
/// where a readiness check bounded by the orchestrator's own probe timeout cannot.
/// </para>
/// </summary>
public sealed class StuckSagaMetricsCollector : BackgroundService
{
    private readonly StuckSagaMetrics _stuckSagaMetrics;
    private readonly StuckSagaOptions _stuckSagaOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StuckSagaMetricsCollector> _logger;

    public StuckSagaMetricsCollector(
        StuckSagaMetrics stuckSagaMetrics,
        IOptions<StuckSagaOptions> options,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<StuckSagaMetricsCollector> logger)
    {
        _stuckSagaMetrics = stuckSagaMetrics;
        _stuckSagaOptions = options.Value;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Stuck-saga sweep started with interval: {SweepIntervalSeconds}s",
            _stuckSagaOptions.SweepIntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_stuckSagaOptions.SweepIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Swallowed so one failed sweep does not end the loop. The published count then
                // goes stale rather than wrong; a database outage is already the "Saga DB" check's
                // to report.
                _logger.LogError(ex, "Stuck-saga sweep failed");
            }
        }

        _logger.LogInformation("Stuck-saga sweep stopped");
    }

    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sagaDbContext = scope.ServiceProvider.GetRequiredService<SagaDbContext>();

        var stuckBefore = _timeProvider.GetUtcNow() -
                          TimeSpan.FromMinutes(_stuckSagaOptions.StuckSagaThresholdMinutes);

        var stuckPaymentSagaCount = await sagaDbContext.Set<PaymentProcessingSagaState>()
            .CountAsync(
                state => !PaymentProcessingSagaState.TerminalStates.Contains(state.CurrentState) &&
                         state.LastModifiedUtc < stuckBefore,
                cancellationToken);

        _stuckSagaMetrics.SetStuckPaymentSagaCount(stuckPaymentSagaCount);

        if (stuckPaymentSagaCount >= _stuckSagaOptions.MaxStuckSagasBeforeDegraded)
        {
            _logger.LogWarning(
                "Found {StuckPaymentSagaCount} payment sagas with no progress in {ThresholdMinutes} minutes",
                stuckPaymentSagaCount,
                _stuckSagaOptions.StuckSagaThresholdMinutes);
        }
    }
}
