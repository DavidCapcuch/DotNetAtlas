using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment times out.
/// </summary>
public sealed class PaymentTimeoutActivity
    : IStateMachineActivity<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired>
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
        BehaviorContext<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired> context,
        IBehavior<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired> next)
    {
        var saga = context.Saga;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentTimeoutActivity), saga.CorrelationId, SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
        }

        SubscriptionSagaInstrumentation.RecordPaymentTimeout(SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired, TException> context,
        IBehavior<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
