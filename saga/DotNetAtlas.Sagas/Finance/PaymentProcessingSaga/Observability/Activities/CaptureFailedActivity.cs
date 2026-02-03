using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment capture fails.
/// </summary>
public sealed class
    CaptureFailedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentCaptureFailedSagaEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("capture-failed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentCaptureFailedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentCaptureFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(CaptureFailedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
            activity.SetTag(PaymentSagaActivityTags.IsRetryable, message.IsRetryable);
        }

        PaymentSagaInstrumentation.RecordCaptureFailed(message.ErrorCode);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentCaptureFailedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentCaptureFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
