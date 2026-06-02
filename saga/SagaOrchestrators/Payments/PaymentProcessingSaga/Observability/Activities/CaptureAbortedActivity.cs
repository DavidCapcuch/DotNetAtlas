using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records traces and logs when the Checkout saga aborts capture (its confirmation
/// step failed) — the <c>AwaitingCaptureApproval → VoidInProgress</c> transition where the
/// sub-saga issues a pre-capture <c>VoidPaymentCommand</c> (ADR-0026).
/// </summary>
public sealed class
    CaptureAbortedActivity : IStateMachineActivity<PaymentProcessingSagaState, AbortCaptureSagaEvent>
{
    private readonly ILogger<CaptureAbortedActivity> _logger;

    public CaptureAbortedActivity(ILogger<CaptureAbortedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("capture-aborted-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, AbortCaptureSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, AbortCaptureSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(CaptureAbortedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.AuthorizationId, saga.AuthorizationId);
        }

        _logger.LogInformation(
            "{SagaType} {CorrelationId} capture aborted by Checkout saga - voiding authorization. Reason: {Reason}",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, context.Message.Reason);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, AbortCaptureSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, AbortCaptureSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
