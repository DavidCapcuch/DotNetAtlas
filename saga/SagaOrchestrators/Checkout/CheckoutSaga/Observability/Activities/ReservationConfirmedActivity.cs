using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires per individual <see cref="ReservationConfirmedSagaEvent"/> while the
/// saga is in <c>AwaitingConfirmation</c>. Purely informational - tracks Inventory's confirm
/// acknowledgements but does NOT gate the transition to <c>Confirmed</c>; Ordering's
/// <c>OrderConfirmedEvent</c> is the gate per § 4 row 10.
/// </summary>
public sealed class
    ReservationConfirmedActivity : IStateMachineActivity<CheckoutSagaState, ReservationConfirmedSagaEvent>
{
    private readonly ILogger<ReservationConfirmedActivity> _logger;

    public ReservationConfirmedActivity(ILogger<ReservationConfirmedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("reservation-confirmed-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, ReservationConfirmedSagaEvent> context,
        IBehavior<CheckoutSagaState, ReservationConfirmedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(ReservationConfirmedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(CheckoutSagaActivityTags.OrderId, saga.OrderId?.ToString() ?? string.Empty);
            activity.SetTag(CheckoutSagaActivityTags.ProductId, message.ProductId.ToString());
            activity.SetTag(CheckoutSagaActivityTags.ReservationId, message.ReservationId.ToString());
        }

        _logger.LogInformation(
            "{SagaType} {CorrelationId} reservation confirmed. ProductId: {ProductId}, ReservationId: {ReservationId}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.ProductId, message.ReservationId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, ReservationConfirmedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, ReservationConfirmedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
