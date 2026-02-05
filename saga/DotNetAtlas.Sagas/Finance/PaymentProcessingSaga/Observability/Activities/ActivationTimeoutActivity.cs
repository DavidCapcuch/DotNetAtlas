using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when subscription activation times out.
/// </summary>
public sealed class
    ActivationTimeoutActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentActivationTimeoutExpired>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("activation-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentActivationTimeoutExpired> context,
        IBehavior<PaymentProcessingSagaState, PaymentActivationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.InitiatedAtUtc;

        using var activity =
            PaymentProcessingSagaInstrumentation.StartActivity(nameof(ActivationTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.TimeoutStage, "activation");
        }

        PaymentProcessingSagaInstrumentation.RecordSagaTimeout("activation", duration);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentActivationTimeoutExpired, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentActivationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
