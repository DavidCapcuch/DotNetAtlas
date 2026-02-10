using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability;

/// <summary>
/// MassTransit state observer for logging <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/> state transitions.
/// </summary>
/// <remarks>
/// This observer provides centralized logging for all state transitions.
/// Detailed tracing and metrics are handled by individual <see cref="IStateMachineActivity{TInstance,TData}"/>
/// implementations for more granular per-event observability.
/// </remarks>
public sealed class AlertSubscriptionSagaStateObserver(ILogger<AlertSubscriptionSagaStateObserver> logger)
    : IStateObserver<AlertSubscriptionPurchaseSagaState>
{
    public Task StateChanged(
        BehaviorContext<AlertSubscriptionPurchaseSagaState> context,
        State? currentState,
        State? previousState)
    {
        var saga = context.Saga;

        logger.LogInformation(
            "{SagaType} {CorrelationId} state changed: {PreviousState} -> {CurrentState} " +
            "(User: {UserId}, Tier: {SubscriptionTier})",
            nameof(AlertSubscriptionPurchaseSagaOrchestrator), saga.CorrelationId, previousState?.Name ?? "None",
            currentState?.Name ?? "None", saga.UserId, saga.SubscriptionTier);

        return Task.CompletedTask;
    }
}
