using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment capture completes.
/// </summary>
public sealed class
    CaptureCompletedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentCapturedSagaEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("capture-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentCapturedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentCapturedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(CaptureCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.PaymentTransactionId, context.Message.PaymentTransactionId.ToString());
        }

        PaymentSagaInstrumentation.RecordCaptureCompleted();

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentCapturedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentCapturedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
