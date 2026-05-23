using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Schedules;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment saga finalizes successfully
/// (no refund was requested within the finalization window)
/// for the <see cref="PaymentProcessingSagaOrchestrator"/>.
/// </summary>
public sealed class
    SuccessFinalizationActivity : IStateMachineActivity<PaymentProcessingSagaState, SuccessFinalizationTimeoutExpired>
{
    private readonly ILogger<SuccessFinalizationActivity> _logger;
    private readonly TimeProvider _timeProvider;

    public SuccessFinalizationActivity(
        ILogger<SuccessFinalizationActivity> logger,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("success-finalization-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, SuccessFinalizationTimeoutExpired> context,
        IBehavior<PaymentProcessingSagaState, SuccessFinalizationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow().UtcDateTime - saga.InitiatedAtUtc;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(SuccessFinalizationActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.PaymentTransactionId, saga.PaymentTransactionId?.ToString());
        }

        PaymentProcessingSagaMetrics.RecordSagaCompleted(duration);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} finalizing after success timeout - no refund requested",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, SuccessFinalizationTimeoutExpired, TException> context,
        IBehavior<PaymentProcessingSagaState, SuccessFinalizationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
