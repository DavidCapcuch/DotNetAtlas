using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when activation times out.
/// </summary>
public sealed class
    ActivationTimeoutActivity : IStateMachineActivity<AlertSubscriptionPurchaseSagaState, ActivationTimeoutExpired>
{
    private readonly TimeProvider _timeProvider;

    public ActivationTimeoutActivity(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("activation-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionPurchaseSagaState, ActivationTimeoutExpired> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, ActivationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(ActivationTimeoutActivity), saga.CorrelationId, SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.SubscriptionTier, saga.SubscriptionTier.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordSagaTimeout(
            duration, SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, ActivationTimeoutExpired, TException> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, ActivationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
