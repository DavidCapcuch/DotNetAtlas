using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment capture completes successfully
/// for the <see cref="PaymentProcessingSagaOrchestrator"/>.
/// </summary>
public sealed class
    CaptureCompletedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentCapturedSagaEvent>
{
    private readonly ILogger<CaptureCompletedActivity> _logger;

    public CaptureCompletedActivity(ILogger<CaptureCompletedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("capture-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentCapturedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentCapturedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(CaptureCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.PaymentTransactionId, context.Message.PaymentTransactionId.ToString());
        }

        PaymentProcessingSagaMetrics.RecordCaptureCompleted();

        _logger.LogInformation(
            "{SagaType} {CorrelationId} capture completed. TransactionId: {PaymentTransactionId}",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, saga.PaymentTransactionId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentCapturedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentCapturedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
