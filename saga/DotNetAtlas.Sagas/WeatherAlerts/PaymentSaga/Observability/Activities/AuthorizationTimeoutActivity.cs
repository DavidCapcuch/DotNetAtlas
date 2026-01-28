using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment authorization times out.
/// </summary>
public sealed class AuthorizationTimeoutActivity : IStateMachineActivity<PaymentSagaState, AuthorizationTimeoutExpired>
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
        BehaviorContext<PaymentSagaState, AuthorizationTimeoutExpired> context,
        IBehavior<PaymentSagaState, AuthorizationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.InitiatedAtUtc;

        using var activity = PaymentSagaInstrumentation.StartActivity(nameof(AuthorizationTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.timeout_stage", "authorization");
        }

        PaymentSagaInstrumentation.RecordSagaTimeout("authorization", duration);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, AuthorizationTimeoutExpired, TException> context,
        IBehavior<PaymentSagaState, AuthorizationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

