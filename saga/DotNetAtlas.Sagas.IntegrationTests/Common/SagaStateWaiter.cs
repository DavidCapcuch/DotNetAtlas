using DotNetAtlas.Sagas.Common;
using DotNetAtlas.Sagas.Common.SagaAbstractions;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Sagas.IntegrationTests.Common;

/// <summary>
/// Helper class for waiting on saga state transitions in integration tests.
/// Provides polling-based waiting similar to MassTransit's SagaHarness.Exists() but queries the database directly.
/// </summary>
public static class SagaStateWaiter
{
    private const int DefaultPollingIntervalMs = 100;

    /// <summary>
    /// Waits for a saga to reach a specific state by polling the database.
    /// </summary>
    /// <typeparam name="TSagaState">The saga state type.</typeparam>
    /// <param name="dbContext">The DbContext to query.</param>
    /// <param name="correlationId">The correlation ID of the saga instance.</param>
    /// <param name="expectedState">The expected state name (e.g., "VoidInProgress").</param>
    /// <param name="timeout">Maximum time to wait for the state transition.</param>
    /// <param name="pollingIntervalMs">Interval between database polls in milliseconds.</param>
    /// <returns>The saga state once it reaches the expected state.</returns>
    /// <exception cref="TimeoutException">Thrown if the saga doesn't reach the expected state within the timeout.</exception>
    public static async Task<TSagaState> WaitForSagaStateAsync<TSagaState>(
        DbContext dbContext,
        Guid correlationId,
        string expectedState,
        TimeSpan timeout,
        int pollingIntervalMs = DefaultPollingIntervalMs)
        where TSagaState : class, ISagaStateInstance
    {
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            var state = await dbContext.Set<TSagaState>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

            if (state?.CurrentState == expectedState)
            {
                return state;
            }

            await Task.Delay(pollingIntervalMs);
        }

        // Final check to get the actual state for a better error message
        var finalState = await dbContext.Set<TSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var actualState = finalState?.CurrentState ?? "not found";

        throw new TimeoutException(
            $"Saga {typeof(TSagaState).Name} with CorrelationId {correlationId} did not reach state '{expectedState}' within {timeout.TotalSeconds}s. " +
            $"Actual state: '{actualState}'");
    }

    /// <summary>
    /// Waits for a saga to be finalized (removed from the database).
    /// </summary>
    /// <typeparam name="TSagaState">The saga state type.</typeparam>
    /// <param name="dbContext">The DbContext to query.</param>
    /// <param name="correlationId">The correlation ID of the saga instance.</param>
    /// <param name="timeout">Maximum time to wait for finalization.</param>
    /// <param name="pollingIntervalMs">Interval between database polls in milliseconds.</param>
    /// <exception cref="TimeoutException">Thrown if the saga is not finalized within the timeout.</exception>
    public static async Task<bool> WaitForSagaFinalizedAsync<TSagaState>(
        DbContext dbContext,
        Guid correlationId,
        TimeSpan timeout,
        int pollingIntervalMs = DefaultPollingIntervalMs)
        where TSagaState : class, SagaStateMachineInstance
    {
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            var exists = await dbContext.Set<TSagaState>()
                .AsNoTracking()
                .AnyAsync(x => x.CorrelationId == correlationId);

            if (!exists)
            {
                return true;
            }

            await Task.Delay(pollingIntervalMs);
        }

        var finalState = await dbContext.Set<TSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var stateInfo = finalState switch
        {
            ISagaStateInstance auditable => $"Current state: '{auditable.CurrentState}'",
            not null => "Saga still exists",
            _ => "Unknown"
        };

        throw new TimeoutException(
            $"Saga {typeof(TSagaState).Name} with CorrelationId {correlationId} " +
            $"was not finalized within {timeout.TotalSeconds}s. {stateInfo}");
    }
}
