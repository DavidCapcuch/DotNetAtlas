using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires once on the fan-in transition <c>AwaitingStockReservation -&gt;
/// AwaitingPayment</c> when <c>PendingReservations</c> reaches zero. Emits the stock-reservation
/// duration histogram per § 11.2. Bound to <see cref="StockReservedSagaEvent"/> alongside
/// <see cref="StockReservedActivity"/>; the orchestrator's <c>IfElse</c> guard ensures this
/// activity only runs on the last successful reservation.
/// </summary>
public sealed class AllStockReservedActivity : IStateMachineActivity<CheckoutSagaState, StockReservedSagaEvent>
{
    private readonly ILogger<AllStockReservedActivity> _logger;

    public AllStockReservedActivity(ILogger<AllStockReservedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("all-stock-reserved-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, StockReservedSagaEvent> context,
        IBehavior<CheckoutSagaState, StockReservedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(AllStockReservedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(CheckoutSagaActivityTags.ExpectedReservations, saga.ExpectedReservations);
        }

        if (saga is { StockReservationStartedAtUtc: { } started, StockReservationCompletedAtUtc: { } completed })
        {
            CheckoutSagaMetrics.RecordStockReservationDuration(completed - started);
        }

        _logger.LogInformation(
            "{SagaType} {CorrelationId} all {Expected} reservations completed - transitioning to AwaitingPayment",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, saga.ExpectedReservations);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, StockReservedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, StockReservedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
