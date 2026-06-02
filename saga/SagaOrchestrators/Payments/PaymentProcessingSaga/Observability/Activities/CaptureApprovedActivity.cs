using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records traces and logs when the Checkout saga approves capture — the
/// <c>AwaitingCaptureApproval → AwaitingCapture</c> transition where the sub-saga issues
/// <c>CapturePaymentCommand</c> (ADR-0026).
/// </summary>
public sealed class
    CaptureApprovedActivity : IStateMachineActivity<PaymentProcessingSagaState, ApproveCaptureSagaEvent>
{
    private readonly ILogger<CaptureApprovedActivity> _logger;

    public CaptureApprovedActivity(ILogger<CaptureApprovedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("capture-approved-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, ApproveCaptureSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, ApproveCaptureSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(CaptureApprovedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.AuthorizationId, saga.AuthorizationId);
        }

        _logger.LogInformation(
            "{SagaType} {CorrelationId} capture approved by Checkout saga - issuing capture. AuthorizationId: {AuthorizationId}",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, saga.AuthorizationId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, ApproveCaptureSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, ApproveCaptureSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
