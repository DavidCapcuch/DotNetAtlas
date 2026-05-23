using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Schedules;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment refund times out
/// for the <see cref="PaymentProcessingSagaOrchestrator"/>.
/// </summary>
public sealed class RefundTimeoutActivity : IStateMachineActivity<PaymentProcessingSagaState, RefundTimeoutExpired>
{
    private readonly ILogger<RefundTimeoutActivity> _logger;
    private readonly TimeProvider _timeProvider;

    public RefundTimeoutActivity(
        ILogger<RefundTimeoutActivity> logger,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("refund-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, RefundTimeoutExpired> context,
        IBehavior<PaymentProcessingSagaState, RefundTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow().UtcDateTime - saga.InitiatedAtUtc;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(RefundTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.TimeoutStage, "refund");
        }

        PaymentProcessingSagaMetrics.RecordSagaTimeout("refund", duration);

        _logger.LogError(
            "{SagaType} {CorrelationId} refund timed out for user {UserId}. Manual intervention required",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, RefundTimeoutExpired, TException> context,
        IBehavior<PaymentProcessingSagaState, RefundTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
