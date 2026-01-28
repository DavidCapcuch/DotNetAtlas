using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Persistence.Database;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Sagas.Common.Observability;

/// <summary>
/// Health check for saga state machine infrastructure.
/// Verifies database connectivity and checks for stuck sagas.
/// </summary>
public sealed class SagaStateMachineHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<SubscriptionSagaDbContext> _dbContextFactory;
    private readonly ILogger<SagaStateMachineHealthCheck> _logger;
    private readonly SagaHealthCheckOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="SagaStateMachineHealthCheck"/>.
    /// </summary>
    /// <param name="dbContextFactory">Factory for creating saga database contexts.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">The health check options.</param>
    public SagaStateMachineHealthCheck(
        IDbContextFactory<SubscriptionSagaDbContext> dbContextFactory,
        ILogger<SagaStateMachineHealthCheck> logger,
        IOptions<SagaHealthCheckOptions> options)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Check database connectivity
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                return HealthCheckResult.Unhealthy("Cannot connect to saga database");
            }

            // Check for stuck sagas (sagas that have been in a non-final state for too long)
            var stuckSagaThreshold = TimeSpan.FromMinutes(_options.StuckSagaThresholdMinutes);
            var threshold = DateTime.UtcNow - stuckSagaThreshold;

            // Check Purchase sagas
            var stuckPurchaseSagaCount = await dbContext.Set<SubscriptionPurchaseSagaState>()
                .CountAsync(s =>
                    !SubscriptionPurchaseSagaStates.FinalStates.Contains(s.CurrentState) &&
                    s.LastUpdatedAtUtc < threshold,
                cancellationToken);

            // Check Extension sagas
            var stuckExtensionSagaCount = await dbContext.Set<SubscriptionExtensionSagaState>()
                .CountAsync(s =>
                    !SubscriptionExtensionSagaStates.FinalStates.Contains(s.CurrentState) &&
                    s.LastUpdatedAtUtc < threshold,
                cancellationToken);

            var stuckSagaCount = stuckPurchaseSagaCount + stuckExtensionSagaCount;

            if (stuckSagaCount >= _options.MaxStuckSagasBeforeUnhealthy)
            {
                _logger.LogError(
                    "Found {StuckSagaCount} stuck sagas ({StuckPurchaseCount} purchase, {StuckExtensionCount} extension) - no update in {ThresholdMinutes} minutes, exceeds unhealthy threshold of {MaxUnhealthy}",
                    stuckSagaCount,
                    stuckPurchaseSagaCount,
                    stuckExtensionSagaCount,
                    _options.StuckSagaThresholdMinutes,
                    _options.MaxStuckSagasBeforeUnhealthy);

                return HealthCheckResult.Unhealthy(
                    $"Found {stuckSagaCount} stuck sagas, exceeds maximum threshold",
                    data: new Dictionary<string, object>
                    {
                        ["StuckSagaCount"] = stuckSagaCount,
                        ["StuckPurchaseSagaCount"] = stuckPurchaseSagaCount,
                        ["StuckExtensionSagaCount"] = stuckExtensionSagaCount,
                        ["ThresholdMinutes"] = _options.StuckSagaThresholdMinutes,
                        ["MaxUnhealthy"] = _options.MaxStuckSagasBeforeUnhealthy
                    });
            }

            if (stuckSagaCount >= _options.MaxStuckSagasBeforeDegraded)
            {
                _logger.LogWarning(
                    "Found {StuckSagaCount} potentially stuck sagas ({StuckPurchaseCount} purchase, {StuckExtensionCount} extension) - no update in {ThresholdMinutes} minutes",
                    stuckSagaCount,
                    stuckPurchaseSagaCount,
                    stuckExtensionSagaCount,
                    _options.StuckSagaThresholdMinutes);

                return HealthCheckResult.Degraded(
                    $"Found {stuckSagaCount} potentially stuck sagas",
                    data: new Dictionary<string, object>
                    {
                        ["StuckSagaCount"] = stuckSagaCount,
                        ["StuckPurchaseSagaCount"] = stuckPurchaseSagaCount,
                        ["StuckExtensionSagaCount"] = stuckExtensionSagaCount,
                        ["ThresholdMinutes"] = _options.StuckSagaThresholdMinutes,
                        ["MaxDegraded"] = _options.MaxStuckSagasBeforeDegraded
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
