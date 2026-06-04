using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when Inventory cannot satisfy a stock reservation - drives the
/// transition <c>AwaitingStockReservation -&gt; CompensatingStockReservations</c> per § 4.
/// </summary>
public sealed class
    StockReservationFailedActivity : IStateMachineActivity<CheckoutSagaState, StockReservationFailedSagaEvent>
{
    private readonly ILogger<StockReservationFailedActivity> _logger;

    public StockReservationFailedActivity(ILogger<StockReservationFailedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("stock-reservation-failed-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, StockReservationFailedSagaEvent> context,
        IBehavior<CheckoutSagaState, StockReservationFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(StockReservationFailedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(CheckoutSagaActivityTags.ProductId, message.ProductId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, CheckoutSagaErrorCodes.StockUnavailable);
        }

        CheckoutSagaMetrics.RecordStockReservationFailed("Unavailable");

        _logger.LogWarning(
            "{SagaType} {CorrelationId} stock reservation failed. ProductId: {ProductId}, Requested: {Requested}, Available: {Available}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.ProductId, message.RequestedQuantity,
            message.AvailableQuantity);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, StockReservationFailedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, StockReservationFailedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
