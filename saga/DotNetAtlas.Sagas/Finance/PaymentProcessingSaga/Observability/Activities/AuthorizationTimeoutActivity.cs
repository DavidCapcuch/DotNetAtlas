using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment authorization times out.
/// </summary>
public sealed class
    AuthorizationTimeoutActivity : IStateMachineActivity<PaymentProcessingSagaState, AuthorizationTimeoutExpired>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("authorization-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, AuthorizationTimeoutExpired> context,
        IBehavior<PaymentProcessingSagaState, AuthorizationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.InitiatedAtUtc;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(AuthorizationTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.TimeoutStage, "authorization");
        }

        PaymentSagaInstrumentation.RecordSagaTimeout("authorization", duration);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, AuthorizationTimeoutExpired, TException> context,
        IBehavior<PaymentProcessingSagaState, AuthorizationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
