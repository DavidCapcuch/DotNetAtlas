using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when subscription activation completes.
/// </summary>
public sealed class
    ActivationCompletedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentActivationCompletedSagaEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("activation-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentActivationCompletedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentActivationCompletedSagaEvent> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.InitiatedAtUtc;

        using var activity =
            PaymentProcessingSagaInstrumentation.StartActivity(nameof(ActivationCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.PaymentTransactionId, saga.PaymentTransactionId?.ToString());
        }

        PaymentProcessingSagaInstrumentation.RecordSagaCompleted(duration);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentActivationCompletedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentActivationCompletedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
