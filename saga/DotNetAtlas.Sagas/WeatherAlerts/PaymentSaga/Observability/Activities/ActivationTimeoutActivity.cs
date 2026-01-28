using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when subscription activation times out.
/// </summary>
public sealed class ActivationTimeoutActivity : IStateMachineActivity<PaymentSagaState, PaymentActivationTimeoutExpired>
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
        BehaviorContext<PaymentSagaState, PaymentActivationTimeoutExpired> context,
        IBehavior<PaymentSagaState, PaymentActivationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.InitiatedAtUtc;

        using var activity = PaymentSagaInstrumentation.StartActivity(nameof(ActivationTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.timeout_stage", "activation");
        }

        PaymentSagaInstrumentation.RecordSagaTimeout("activation", duration);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, PaymentActivationTimeoutExpired, TException> context,
        IBehavior<PaymentSagaState, PaymentActivationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

