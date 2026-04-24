using MassTransit;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability;

/// <summary>
/// MassTransit state observer for logging <see cref="PaymentProcessingSagaOrchestrator"/> state transitions.
/// </summary>
/// <remarks>
/// This observer provides centralized logging for all state transitions.
/// Detailed tracing and metrics are handled by individual <see cref="IStateMachineActivity{TInstance,TData}"/>
/// implementations for more granular per-event observability.
/// </remarks>
public sealed class PaymentSagaStateObserver(ILogger<PaymentSagaStateObserver> logger)
    : IStateObserver<PaymentProcessingSagaState>
{
    public Task StateChanged(
        BehaviorContext<PaymentProcessingSagaState> context,
        State? currentState,
        State? previousState)
    {
        var saga = context.Saga;

        logger.LogInformation(
            "{SagaType} {CorrelationId} state changed: {PreviousState} -> {CurrentState} " +
            "(User: {UserId}, Amount: {Amount} {Currency})",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, previousState?.Name ?? "None",
            currentState?.Name ?? "None", saga.UserId, saga.Amount, saga.Currency);

        return Task.CompletedTask;
    }
}
