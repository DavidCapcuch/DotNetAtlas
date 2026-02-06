using System.Linq.Expressions;
using DotNetAtlas.Sagas.Common;
using DotNetAtlas.Sagas.Common.SagaAbstractions;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Sagas.IntegrationTests.Common;

/// <summary>
/// Typed helper for waiting on saga state transitions in integration tests.
/// Provides a fluent API similar to MassTransit's ISagaStateMachineTestHarness.
/// </summary>
/// <typeparam name="TSaga">The saga state machine type.</typeparam>
/// <typeparam name="TSagaState">The saga state instance type.</typeparam>
public sealed class SagaStateMonitor<TSaga, TSagaState>
    where TSaga : MassTransitStateMachine<TSagaState>
    where TSagaState : class, ISagaStateInstance
{
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
    public Task<TSagaState> WaitForStateAsync(
        Guid correlationId,
        Expression<Func<TSaga, State>> stateSelector,
        TimeSpan timeout)
    {
        var stateName = GetStateName(stateSelector);
        return SagaStateWaiter.WaitForSagaStateAsync<TSagaState>(_dbContext, correlationId, stateName, timeout);
    }

    /// <summary>
    /// Waits for a saga to be finalized (removed from the database).
    /// </summary>
    /// <param name="correlationId">The correlation ID of the saga instance.</param>
    /// <param name="timeout">Maximum time to wait for finalization.</param>
    /// <exception cref="TimeoutException">Thrown if the saga is not finalized within the timeout.</exception>
    public Task<bool> WaitForFinalizedAsync(Guid correlationId, TimeSpan timeout)
    {
        return SagaStateWaiter.WaitForSagaFinalizedAsync<TSagaState>(_dbContext, correlationId, timeout);
    }

    private string GetStateName(Expression<Func<TSaga, State>> stateSelector)
    {
        var compiledSelector = stateSelector.Compile();
        var state = compiledSelector(_stateMachine);

        return state.Name;
    }
}

