using System.Linq.Expressions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Platform.Test.Framework.Common;
using SagaOrchestrators.Common.SagaAbstractions;

namespace SagaOrchestrators.IntegrationTests.Common;

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
    /// <summary>Bound on the post-timeout read that names the observed state in the failure message.</summary>
    private static readonly TimeSpan DiagnosticReadTimeout = TimeSpan.FromSeconds(5);

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
    /// <exception cref="EventuallyTimeoutException">Thrown if the saga doesn't reach the expected state within the timeout.</exception>
    public async Task<TSagaState> WaitForStateAsync(
        Guid correlationId,
        Expression<Func<TSaga, State>> stateSelector,
        TimeSpan timeout)
    {
        var stateName = GetStateName(stateSelector);
        TSagaState? observed = null;

        try
        {
            await Eventually.UntilAsync(
                async token =>
                {
                    observed = await _dbContext.Set<TSagaState>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.CorrelationId == correlationId, token);

                    return observed?.CurrentState == stateName;
                },
                timeout,
                $"saga {typeof(TSagaState).Name} with CorrelationId {correlationId} to reach state '{stateName}'");

            return observed!;
        }
        catch (EventuallyTimeoutException expired)
        {
            var actualState = await DescribeStateAsync(correlationId, "not found");

            throw new EventuallyTimeoutException(
                $"Saga {typeof(TSagaState).Name} with CorrelationId {correlationId} " +
                $"did not reach state '{stateName}' within {timeout.TotalSeconds}s. " +
                $"Actual state: '{actualState}'",
                expired);
        }
    }

    /// <summary>
    /// Waits until no row with <paramref name="observed"/>'s correlation id remains — which is how
    /// a finalized saga presents, since the orchestrator's <c>SetCompletedWhenFinalized()</c>
    /// deletes the row on <c>Finalize()</c>.
    /// </summary>
    /// <param name="observed">
    /// The most recent state a wait read back. The poll cannot tell a finalized saga from one that
    /// never started — both are "no row", and the second answers on the first poll — so taking proof
    /// the saga existed is what rules that case out. Only existence is enforced: pass an older
    /// observation and the wait still holds, but a failure names a state the saga has since left.
    /// </param>
    /// <param name="timeout">Maximum time to wait for finalization.</param>
    /// <returns>
    /// The outcome. Assert on it — <c>Should().BeFinalized()</c>; observing alone verifies nothing,
    /// because a saga that never finalizes is a returned <c>false</c> here, not a throw.
    /// </returns>
    public async Task<SagaFinalization> ObserveFinalizationAsync(TSagaState observed, TimeSpan timeout)
    {
        var correlationId = observed.CorrelationId;

        try
        {
            await Eventually.UntilAsync(
                async token => !await _dbContext.Set<TSagaState>()
                    .AsNoTracking()
                    .AnyAsync(x => x.CorrelationId == correlationId, token),
                timeout,
                $"saga {typeof(TSagaState).Name} with CorrelationId {correlationId} to be finalized");

            return Outcome(isFinalized: true, currentState: "gone");
        }
        catch (EventuallyTimeoutException)
        {
            return Outcome(isFinalized: false, await DescribeStateAsync(correlationId, "gone"));
        }

        SagaFinalization Outcome(bool isFinalized, string currentState) => new(
            isFinalized,
            typeof(TSagaState).Name,
            correlationId,
            timeout,
            observed.CurrentState,
            currentState);
    }

    /// <summary>
    /// Names the saga's current state for a failure message. Always yields a word and never throws:
    /// a read taken to describe a timeout must not become a second failure that replaces it.
    /// </summary>
    /// <remarks>
    /// Read fresh rather than reusing whatever the last probe saw, which can be seconds stale when
    /// that probe was the one cut short — and bounded, because the database being slow is a likely
    /// reason for arriving here at all.
    /// </remarks>
    private async Task<string> DescribeStateAsync(Guid correlationId, string whenMissing)
    {
        using var diagnostic = new CancellationTokenSource(DiagnosticReadTimeout);

        try
        {
            var state = await _dbContext.Set<TSagaState>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CorrelationId == correlationId, diagnostic.Token);

            return state?.CurrentState ?? whenMissing;
        }
        catch (OperationCanceledException)
        {
            return "unknown (the state read timed out too)";
        }
    }

    private string GetStateName(Expression<Func<TSaga, State>> stateSelector)
    {
        var compiledSelector = stateSelector.Compile();
        var state = compiledSelector(_stateMachine);

        return state.Name;
    }
}
