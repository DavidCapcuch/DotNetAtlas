using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Schedules;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment capture times out
/// for the <see cref="PaymentProcessingSagaOrchestrator"/>.
/// </summary>
public sealed class CaptureTimeoutActivity : IStateMachineActivity<PaymentProcessingSagaState, CaptureTimeoutExpired>
{
    private readonly ILogger<CaptureTimeoutActivity> _logger;
    private readonly TimeProvider _timeProvider;

    public CaptureTimeoutActivity(
        ILogger<CaptureTimeoutActivity> logger,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("capture-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, CaptureTimeoutExpired> context,
        IBehavior<PaymentProcessingSagaState, CaptureTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow().UtcDateTime - saga.InitiatedAtUtc;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(CaptureTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, PaymentProcessingSagaErrorCodes.CaptureTimeout);
            activity.SetTag(PaymentSagaActivityTags.TimeoutStage, PaymentSagaActivityTags.TimeoutStages.Capture);
        }

        PaymentProcessingSagaMetrics.RecordSagaTimeout(PaymentSagaActivityTags.TimeoutStages.Capture, duration);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} capture timed out for user {UserId}",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, CaptureTimeoutExpired, TException> context,
        IBehavior<PaymentProcessingSagaState, CaptureTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
