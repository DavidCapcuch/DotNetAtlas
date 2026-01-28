using MassTransit;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Observability;

/// <summary>
/// MassTransit state observer for logging saga state transitions.
/// </summary>
/// <remarks>
/// This observer provides centralized logging for all state transitions.
/// Detailed tracing and metrics are handled by individual <see cref="IStateMachineActivity{TInstance,TData}"/>
/// implementations for more granular per-event observability.
/// </remarks>
public sealed class SubscriptionSagaStateObserver(ILogger<SubscriptionSagaStateObserver> logger)
    : IStateObserver<SubscriptionPurchaseSagaState>
{
    public Task StateChanged(
        BehaviorContext<SubscriptionPurchaseSagaState> context,
        State currentState,
        State previousState)
    {
        var saga = context.Saga;

        logger.LogInformation(
            "Saga {CorrelationId} state changed: {PreviousState} -> {CurrentState} (User: {UserId}, Tier: {SubscriptionTier})",
            saga.CorrelationId,
            previousState.Name,
            currentState.Name,
            saga.UserId,
            saga.SubscriptionTier);

        return Task.CompletedTask;
    }
}
