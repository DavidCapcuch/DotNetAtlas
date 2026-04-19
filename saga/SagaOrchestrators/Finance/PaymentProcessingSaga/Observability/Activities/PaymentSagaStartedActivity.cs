using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when the <see cref="PaymentProcessingSagaOrchestrator"/> starts
/// processing a new payment request.
/// </summary>
public sealed class
    PaymentSagaStartedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentInitiatedSagaEvent>
{
    private readonly ILogger<PaymentSagaStartedActivity> _logger;

    public PaymentSagaStartedActivity(ILogger<PaymentSagaStartedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-saga-started-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentInitiatedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentInitiatedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(PaymentSagaStartedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.Amount, saga.Amount);
            activity.SetTag(PaymentSagaActivityTags.Currency, saga.Currency);
        }

        PaymentProcessingSagaMetrics.RecordSagaStarted(saga.Currency);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} initialized for user {UserId}, amount {Amount} {Currency}",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, saga.UserId, saga.Amount, saga.Currency);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentInitiatedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentInitiatedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
