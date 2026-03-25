using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Finance.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when a refund is requested
/// for the <see cref="PaymentProcessingSagaOrchestrator"/>.
/// </summary>
public sealed class
    RefundRequestedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentRefundRequestedSagaEvent>
{
    private readonly ILogger<RefundRequestedActivity> _logger;

    public RefundRequestedActivity(ILogger<RefundRequestedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("refund-requested-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentRefundRequestedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentRefundRequestedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(RefundRequestedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.PaymentTransactionId, saga.PaymentTransactionId?.ToString());
        }

        PaymentProcessingSagaMetrics.RecordRefundRequested();

        _logger.LogInformation(
            "{SagaType} {CorrelationId} received refund request for user {UserId}. Reason: {Reason}",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, saga.UserId, message.Reason);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentRefundRequestedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentRefundRequestedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
