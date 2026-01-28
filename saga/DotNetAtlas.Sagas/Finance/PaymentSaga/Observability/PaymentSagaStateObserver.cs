using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Observability;

/// <summary>
/// MassTransit state observer for logging payment saga state transitions.
/// </summary>
/// <remarks>
/// This observer provides centralized logging for all state transitions.
/// Detailed tracing and metrics are handled by individual <see cref="IStateMachineActivity{TInstance,TData}"/>
/// implementations for more granular per-event observability.
/// </remarks>
public sealed class PaymentSagaStateObserver(ILogger<PaymentSagaStateObserver> logger)
    : IStateObserver<PaymentSagaState>
{
    public Task StateChanged(
        BehaviorContext<PaymentSagaState> context,
        State currentState,
        State previousState)
    {
        var saga = context.Saga;

        logger.LogInformation(
            "Payment saga {CorrelationId} state changed: {PreviousState} -> {CurrentState} " +
            "(User: {UserId}, Amount: {Amount} {Currency})",
            saga.CorrelationId, previousState.Name, currentState.Name, saga.UserId, saga.Amount, saga.Currency);

        return Task.CompletedTask;
    }
}
