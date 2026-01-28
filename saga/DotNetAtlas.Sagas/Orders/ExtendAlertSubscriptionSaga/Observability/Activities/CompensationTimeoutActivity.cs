using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when compensation times out.
/// </summary>
public sealed class CompensationTimeoutActivity
    : IStateMachineActivity<SubscriptionExtensionSagaState, CompensationTimeoutExpired>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("compensation-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionExtensionSagaState, CompensationTimeoutExpired> context,
        IBehavior<SubscriptionExtensionSagaState, CompensationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(CompensationTimeoutActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordCompensationTimeout(
            duration,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionExtensionSagaState, CompensationTimeoutExpired, TException> context,
        IBehavior<SubscriptionExtensionSagaState, CompensationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
