using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment refund completes successfully
/// for the <see cref="PaymentProcessingSaga"/>.
/// </summary>
public sealed class
    RefundCompletedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentRefundCompletedSagaEvent>
{
    private readonly ILogger<RefundCompletedActivity> _logger;

    public RefundCompletedActivity(ILogger<RefundCompletedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("refund-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentRefundCompletedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentRefundCompletedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentProcessingSagaInstrumentation.StartActivity(nameof(RefundCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.RefundTransactionId, context.Message.RefundTransactionId.ToString());
        }

        PaymentProcessingSagaInstrumentation.RecordRefundCompleted();

        _logger.LogInformation(
            "{SagaType} {CorrelationId} refund completed. RefundTransactionId: {RefundTransactionId}",
            nameof(PaymentProcessingSaga), saga.CorrelationId, context.Message.RefundTransactionId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentRefundCompletedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentRefundCompletedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
