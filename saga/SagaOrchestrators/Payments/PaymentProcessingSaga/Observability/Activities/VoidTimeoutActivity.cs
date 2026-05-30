using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Schedules;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment void times out
/// for the <see cref="PaymentProcessingSagaOrchestrator"/>.
/// </summary>
public sealed class VoidTimeoutActivity : IStateMachineActivity<PaymentProcessingSagaState, VoidTimeoutExpired>
{
    private readonly ILogger<VoidTimeoutActivity> _logger;
    private readonly TimeProvider _timeProvider;

    public VoidTimeoutActivity(
        ILogger<VoidTimeoutActivity> logger,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("void-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, VoidTimeoutExpired> context,
        IBehavior<PaymentProcessingSagaState, VoidTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow().UtcDateTime - saga.InitiatedAtUtc;

        using var activity = PaymentProcessingSagaMetrics.StartActivity(nameof(VoidTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, PaymentProcessingSagaErrorCodes.VoidTimeout);
            activity.SetTag(PaymentSagaActivityTags.TimeoutStage, "void");
        }

        PaymentProcessingSagaMetrics.RecordSagaTimeout("void", duration);

        _logger.LogError(
            "{SagaType} {CorrelationId} void timed out for user {UserId}. Manual intervention required",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, VoidTimeoutExpired, TException> context,
        IBehavior<PaymentProcessingSagaState, VoidTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
