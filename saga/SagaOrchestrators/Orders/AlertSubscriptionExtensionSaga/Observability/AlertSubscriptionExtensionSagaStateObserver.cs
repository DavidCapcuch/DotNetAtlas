using MassTransit;

namespace SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Observability;

/// <summary>
/// MassTransit state observer for logging <see cref="AlertSubscriptionExtensionSagaOrchestrator"/> state transitions.
/// </summary>
/// <remarks>
/// This observer provides centralized logging for all state transitions.
/// Detailed tracing and metrics are handled by individual <see cref="IStateMachineActivity{TInstance,TData}"/>
/// implementations for more granular per-event observability.
/// </remarks>
public sealed class AlertSubscriptionExtensionSagaStateObserver(
    ILogger<AlertSubscriptionExtensionSagaStateObserver> logger)
    : IStateObserver<AlertSubscriptionExtensionSagaState>
{
    public Task StateChanged(
        BehaviorContext<AlertSubscriptionExtensionSagaState> context,
        State? currentState,
        State? previousState)
    {
        var saga = context.Saga;

        logger.LogInformation(
            "{SagaType} {CorrelationId} state changed: {PreviousState} -> {CurrentState} " +
            "(User: {UserId}, Duration: {DurationDays} days)",
            nameof(AlertSubscriptionExtensionSagaOrchestrator), saga.CorrelationId, previousState?.Name ?? "None",
            currentState?.Name ?? "None", saga.UserId, saga.DurationDays);

        return Task.CompletedTask;
    }
}
