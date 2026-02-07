using System.Linq.Expressions;
using DotNetAtlas.Sagas.Common.SagaAbstractions;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Sagas.IntegrationTests.Common;

/// <summary>
/// Typed helper for waiting on saga state transitions in integration tests.
/// Provides a fluent API similar to MassTransit's ISagaStateMachineTestHarness,
/// but meant to be used in integration tests with real instances and infrastructure.
/// </summary>
/// <typeparam name="TSaga">The saga state machine type.</typeparam>
/// <typeparam name="TSagaState">The saga state instance type.</typeparam>
public sealed class SagaStateMonitor<TSaga, TSagaState>
    where TSaga : MassTransitStateMachine<TSagaState>
    where TSagaState : class, ISagaStateInstance
{
    private const int DefaultPollingIntervalMs = 100;
    private readonly DbContext _dbContext;
    private readonly TSaga _stateMachine;

    /// <summary>
    /// Creates a new saga test helper.
    /// </summary>
    /// <param name="dbContext">The DbContext to query for saga state.</param>
    /// <param name="stateMachine">The saga state machine instance (resolve from DI).</param>
    public SagaStateMonitor(DbContext dbContext, TSaga stateMachine)
    {
        _dbContext = dbContext;
        _stateMachine = stateMachine;
    }

    /// <summary>
    /// Waits for a saga to reach a specific state by polling the database.
    /// </summary>
    /// <param name="correlationId">The correlation ID of the saga instance.</param>
    /// <param name="stateSelector">Expression selecting the target state from the saga state machine (e.g., x => x.VoidInProgress).</param>
    /// <param name="timeout">Maximum time to wait for the state transition.</param>
    /// <returns>The saga state once it reaches the expected state.</returns>
    /// <exception cref="TimeoutException">Thrown if the saga doesn't reach the expected state within the timeout.</exception>
    public async Task<TSagaState> WaitForStateAsync(
        Guid correlationId,
        Expression<Func<TSaga, State>> stateSelector,
        TimeSpan timeout)
    {
        var stateName = GetStateName(stateSelector);
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            var state = await _dbContext.Set<TSagaState>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

            if (state?.CurrentState == stateName)
            {
                return state;
            }

            await Task.Delay(DefaultPollingIntervalMs);
        }

        var finalState = await _dbContext.Set<TSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        if (finalState?.CurrentState == stateName)
        {
            return finalState;
        }

        var actualState = finalState?.CurrentState ?? "not found";
        throw new TimeoutException(
            $"Saga {typeof(TSagaState).Name} with CorrelationId {correlationId} " +
            $"did not reach state '{stateName}' within {timeout.TotalSeconds}s. " +
            $"Actual state: '{actualState}'");
    }

    /// <summary>
    /// Waits for a saga to be finalized (removed from the database).
    /// </summary>
    /// <param name="correlationId">The correlation ID of the saga instance.</param>
    /// <param name="timeout">Maximum time to wait for finalization.</param>
    /// <exception cref="TimeoutException">Thrown if the saga is not finalized within the timeout.</exception>
    public async Task<bool> WaitForFinalizedAsync(Guid correlationId, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            var exists = await _dbContext.Set<TSagaState>()
                .AsNoTracking()
                .AnyAsync(x => x.CorrelationId == correlationId);

            if (!exists)
            {
                return true;
            }

            await Task.Delay(DefaultPollingIntervalMs);
        }

        var finalState = await _dbContext.Set<TSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        if (finalState is null)
        {
            return true;
        }

        throw new TimeoutException(
            $"Saga {typeof(TSagaState).Name} with CorrelationId {correlationId} " +
            $"was not finalized within {timeout.TotalSeconds}s. Current state: {finalState.CurrentState}");
    }

    private string GetStateName(Expression<Func<TSaga, State>> stateSelector)
    {
        var compiledSelector = stateSelector.Compile();
        var state = compiledSelector(_stateMachine);

        return state.Name;
    }
}
