using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires per individual <see cref="ReservationReleasedSagaEvent"/> while the
/// saga is in <c>CompensatingStockReservations</c>. Tagged with the release reason so ops can
/// distinguish compensation-driven releases from TTL expiry per § 7.2.
/// </summary>
public sealed class
    ReservationReleasedActivity : IStateMachineActivity<CheckoutSagaState, ReservationReleasedSagaEvent>
{
    private readonly ILogger<ReservationReleasedActivity> _logger;

    public ReservationReleasedActivity(ILogger<ReservationReleasedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("reservation-released-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, ReservationReleasedSagaEvent> context,
        IBehavior<CheckoutSagaState, ReservationReleasedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(ReservationReleasedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(CheckoutSagaActivityTags.OrderId, saga.OrderId?.ToString() ?? string.Empty);
            activity.SetTag(CheckoutSagaActivityTags.ProductId, message.ProductId.ToString());
            activity.SetTag(CheckoutSagaActivityTags.ReservationId, message.ReservationId.ToString());
            activity.SetTag("saga.release_reason", message.ReleaseReason);
        }

        _logger.LogInformation(
            "{SagaType} {CorrelationId} reservation released. ProductId: {ProductId}, ReservationId: {ReservationId}, Reason: {Reason}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.ProductId, message.ReservationId,
            message.ReleaseReason);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, ReservationReleasedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, ReservationReleasedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
