using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment capture fails
/// for the <see cref="PaymentProcessingSagaOrchestrator"/>.
/// </summary>
public sealed class
    CaptureFailedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentCaptureFailedSagaEvent>
{
    private readonly ILogger<CaptureFailedActivity> _logger;

    public CaptureFailedActivity(ILogger<CaptureFailedActivity> logger)
    {
        _logger = logger;
    }

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
            PaymentProcessingSagaMetrics.StartActivity(nameof(CaptureFailedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
            activity.SetTag(PaymentSagaActivityTags.IsRetryable, message.IsRetryable);
        }

        PaymentProcessingSagaMetrics.RecordCaptureFailed(message.ErrorCode);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} capture failed: {ErrorCode} - {ErrorMessage}",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, message.ErrorCode, message.ErrorMessage);

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
