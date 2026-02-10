using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment void completes successfully
/// for the <see cref="PaymentProcessingSagaOrchestrator"/>.
/// </summary>
public sealed class VoidCompletedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentVoidedSagaEvent>
{
    private readonly ILogger<VoidCompletedActivity> _logger;

    public VoidCompletedActivity(ILogger<VoidCompletedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("void-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentVoidedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentVoidedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentProcessingSagaMetrics.StartActivity(nameof(VoidCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.AuthorizationId, context.Message.AuthorizationId);
        }

        PaymentProcessingSagaMetrics.RecordVoidCompleted();

        _logger.LogInformation(
            "{SagaType} {CorrelationId} void completed. AuthorizationId: {AuthorizationId}",
            nameof(PaymentProcessingSagaOrchestrator), saga.CorrelationId, saga.AuthorizationId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentVoidedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentVoidedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
