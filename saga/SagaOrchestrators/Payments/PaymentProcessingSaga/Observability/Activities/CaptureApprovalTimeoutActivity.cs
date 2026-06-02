using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Schedules;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when the capture-approval wait-state times out
/// (the Checkout saga never signalled approval/abort) for the
/// <see cref="PaymentProcessingSagaOrchestrator"/>. Drives the void path (ADR-0026).
/// </summary>
public sealed class
    CaptureApprovalTimeoutActivity : IStateMachineActivity<PaymentProcessingSagaState, CaptureApprovalTimeoutExpired>
{
    private readonly ILogger<CaptureApprovalTimeoutActivity> _logger;
    private readonly TimeProvider _timeProvider;

    public CaptureApprovalTimeoutActivity(
        ILogger<CaptureApprovalTimeoutActivity> logger,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("capture-approval-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, CaptureApprovalTimeoutExpired> context,
        IBehavior<PaymentProcessingSagaState, CaptureApprovalTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow().UtcDateTime - saga.InitiatedAtUtc;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(CaptureApprovalTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, PaymentProcessingSagaErrorCodes.CaptureApprovalTimeout);
            activity.SetTag(PaymentSagaActivityTags.TimeoutStage, PaymentSagaActivityTags.TimeoutStages.CaptureApproval);
        }

        PaymentProcessingSagaMetrics.RecordSagaTimeout(PaymentSagaActivityTags.TimeoutStages.CaptureApproval, duration);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} capture-approval timed out for user {UserId} - voiding authorization",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, CaptureApprovalTimeoutExpired, TException> context,
        IBehavior<PaymentProcessingSagaState, CaptureApprovalTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
