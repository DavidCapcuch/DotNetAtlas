using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.Schedules;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when <c>StockReservationTimeout</c> expires (transition
/// <c>AwaitingStockReservation -&gt; CompensatingStockReservations</c>) per
/// docs/bc-design/checkout-saga.md § 3. Increments
/// <c>saga.checkout.stock_reservation_timeout</c> counter.
/// </summary>
public sealed class StockReservationTimeoutActivity
    : IStateMachineActivity<CheckoutSagaState, StockReservationTimeoutExpired>
{
    private readonly ILogger<StockReservationTimeoutActivity> _logger;

    public StockReservationTimeoutActivity(ILogger<StockReservationTimeoutActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("stock-reservation-timeout-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, StockReservationTimeoutExpired> context,
        IBehavior<CheckoutSagaState, StockReservationTimeoutExpired> next)
    {
        var saga = context.Saga;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(StockReservationTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, CheckoutSagaErrorCodes.StockTimeout);
            activity.SetTag(CheckoutSagaActivityTags.PendingReservations, saga.PendingReservations);
        }

        CheckoutSagaMetrics.RecordStockReservationTimeout();

        _logger.LogWarning(
            "{SagaType} {CorrelationId} stock reservation timeout fired - {Pending} of {Expected} reservations still pending",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, saga.PendingReservations, saga.ExpectedReservations);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, StockReservationTimeoutExpired, TException> context,
        IBehavior<CheckoutSagaState, StockReservationTimeoutExpired> next)
        where TException : Exception => next.Faulted(context);
}
