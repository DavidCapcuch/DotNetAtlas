using MassTransit;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Observability;

/// <summary>
/// MassTransit state observer for logging Extension Saga state transitions.
/// </summary>
/// <remarks>
/// This observer provides centralized logging for all state transitions.
/// Detailed tracing and metrics are handled by individual <see cref="IStateMachineActivity{TInstance,TData}"/>
/// implementations for more granular per-event observability.
/// </remarks>
public sealed class SubscriptionExtensionSagaStateObserver(ILogger<SubscriptionExtensionSagaStateObserver> logger)
    : IStateObserver<SubscriptionExtensionSagaState>
{
    public Task StateChanged(
        BehaviorContext<SubscriptionExtensionSagaState> context,
        State currentState,
        State previousState)
    {
        var saga = context.Saga;

        logger.LogInformation(
            "Extension Saga {CorrelationId} state changed: {PreviousState} -> {CurrentState} " +
            "(User: {UserId}, Duration: {DurationDays} days)",
            saga.CorrelationId, previousState.Name, currentState.Name, saga.UserId, saga.DurationDays);

        return Task.CompletedTask;
    }
}
