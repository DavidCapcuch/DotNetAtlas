using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment times out.
/// </summary>
public sealed class PaymentTimeoutActivity
    : IStateMachineActivity<SubscriptionExtensionSagaState, PaymentTimeoutExpired>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionExtensionSagaState, PaymentTimeoutExpired> context,
        IBehavior<SubscriptionExtensionSagaState, PaymentTimeoutExpired> next)
    {
        var saga = context.Saga;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentTimeoutActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
        }

        SubscriptionSagaInstrumentation.RecordPaymentTimeout(SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionExtensionSagaState, PaymentTimeoutExpired, TException> context,
        IBehavior<SubscriptionExtensionSagaState, PaymentTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
