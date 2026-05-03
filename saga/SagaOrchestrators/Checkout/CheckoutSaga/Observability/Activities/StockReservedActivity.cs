using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires per individual <see cref="StockReservedSagaEvent"/> while the saga is
/// in <c>AwaitingStockReservation</c>. Multiple invocations per saga - one per distinct
/// ProductId per § 5.2 fan-in.
/// </summary>
public sealed class StockReservedActivity : IStateMachineActivity<CheckoutSagaState, StockReservedSagaEvent>
{
    private readonly ILogger<StockReservedActivity> _logger;

    public StockReservedActivity(ILogger<StockReservedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("stock-reserved-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, StockReservedSagaEvent> context,
        IBehavior<CheckoutSagaState, StockReservedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(StockReservedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(CheckoutSagaActivityTags.OrderId, saga.OrderId?.ToString() ?? string.Empty);
            activity.SetTag(CheckoutSagaActivityTags.ProductId, message.ProductId.ToString());
            activity.SetTag(CheckoutSagaActivityTags.ReservationId, message.ReservationId.ToString());
            activity.SetTag(CheckoutSagaActivityTags.PendingReservations, saga.PendingReservations);
        }

        _logger.LogInformation(
            "{SagaType} {CorrelationId} stock reserved. ProductId: {ProductId}, ReservationId: {ReservationId}, Pending: {Pending}/{Expected}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.ProductId, message.ReservationId,
            saga.PendingReservations, saga.ExpectedReservations);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, StockReservedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, StockReservedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
