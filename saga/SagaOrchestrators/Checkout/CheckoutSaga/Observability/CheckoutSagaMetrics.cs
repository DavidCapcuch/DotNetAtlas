using System.Diagnostics.Metrics;
using SagaOrchestrators.Common.Observability;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability;

/// <summary>
/// OpenTelemetry instrumentation for the Checkout saga per
/// docs/bc-design/checkout-saga.md § 11.2. Counters + histograms cover the event-driven
/// transition surface.
/// </summary>
/// <remarks>
/// Meter is named <see cref="ApplicationInfo.AppName"/> to match the OTel registration in
/// <see cref="SagaOrchestrators.Common.ObservabilityDependencyInjection"/> - the design
/// doc names this meter <c>SagaOrchestrators.Checkout</c>, but per the existing
/// PaymentProcessingSagaMetrics pattern we use the shared meter and let instrument names
/// (<c>saga.checkout.*</c>) provide the logical namespacing.
/// </remarks>
public static class CheckoutSagaMetrics
{
    private static readonly Meter Meter = new(ApplicationInfo.AppName, ApplicationInfo.Version);

    // Counters (M4 - event-driven surface only; timeout counters defer to M5)
    private static readonly Counter<long> Initiated =
        Meter.CreateCounter<long>("saga.checkout.initiated", "count",
            "Number of checkout sagas initiated");

    private static readonly Counter<long> Confirmed =
        Meter.CreateCounter<long>("saga.checkout.confirmed", "count",
            "Number of checkout sagas that reached Confirmed");

    private static readonly Counter<long> Failed =
        Meter.CreateCounter<long>("saga.checkout.failed", "count",
            "Number of checkout sagas that reached Failed");

    private static readonly Counter<long> Compensated =
        Meter.CreateCounter<long>("saga.checkout.compensated", "count",
            "Number of checkout sagas that reached Compensated");

    private static readonly Counter<long> Stuck =
        Meter.CreateCounter<long>("saga.checkout.stuck", "count",
            "Number of checkout sagas that reached CompensationStuck (ops alert)");

    private static readonly Counter<long> StockReservationFailed =
        Meter.CreateCounter<long>("saga.checkout.stock_reservation_failed", "count",
            "Number of stock reservation failures (by reason)");

    private static readonly Counter<long> PaymentFailed =
        Meter.CreateCounter<long>("saga.checkout.payment_failed", "count",
            "Number of payment failures (by reason)");

    // Timeout counters (M5 - one per § 7 timeout kind, tag CheckoutSagaActivityTags.LastState
    // for compensation timeout per § 11.2)
    private static readonly Counter<long> OrderCreationTimeout =
        Meter.CreateCounter<long>("saga.checkout.order_creation_timeout", "count",
            "OrderCreationTimeout fired (saga in AwaitingOrderCreation)");

    private static readonly Counter<long> StockReservationTimeout =
        Meter.CreateCounter<long>("saga.checkout.stock_reservation_timeout", "count",
            "StockReservationTimeout fired (saga in AwaitingStockReservation)");

    private static readonly Counter<long> PaymentTimeout =
        Meter.CreateCounter<long>("saga.checkout.payment_timeout", "count",
            "PaymentTimeout fired (saga in AwaitingPayment)");

    private static readonly Counter<long> ConfirmationTimeout =
        Meter.CreateCounter<long>("saga.checkout.confirmation_timeout", "count",
            "OrderConfirmationTimeout fired (saga in AwaitingConfirmation)");

    private static readonly Counter<long> CompensationTimeout =
        Meter.CreateCounter<long>("saga.checkout.compensation_timeout", "count",
            "CompensationTimeout fired (saga in CompensatingStock/CompensatingPayment)");

    // Histograms - latency observability per § 11.2
    private static readonly Histogram<double> OrderCreationDuration =
        Meter.CreateHistogram<double>("saga.checkout.order_creation_duration_ms", "ms",
            "OrderCreatedAtUtc - InitiatedAtUtc");

    private static readonly Histogram<double> StockReservationDuration =
        Meter.CreateHistogram<double>("saga.checkout.stock_reservation_duration_ms", "ms",
            "StockReservationCompletedAtUtc - StockReservationStartedAtUtc");

    private static readonly Histogram<double> PaymentDuration =
        Meter.CreateHistogram<double>("saga.checkout.payment_duration_ms", "ms",
            "PaymentCompletedAtUtc - PaymentRequestedAtUtc");

    private static readonly Histogram<double> ConfirmationDuration =
        Meter.CreateHistogram<double>("saga.checkout.confirmation_duration_ms", "ms",
            "OrderConfirmedAtUtc - OrderConfirmationRequestedAtUtc");

    private static readonly Histogram<double> TotalDuration =
        Meter.CreateHistogram<double>("saga.checkout.total_duration_ms", "ms",
            "InitiatedAtUtc to terminal timestamp");

    private static readonly Histogram<double> CompensationDuration =
        Meter.CreateHistogram<double>("saga.checkout.compensation_duration_ms", "ms",
            "CompensationCompletedAtUtc - CompensationStartedAtUtc");

    // Up-down counter - currently-executing saga count
    private static readonly UpDownCounter<long> Active =
        Meter.CreateUpDownCounter<long>("saga.checkout.active", "count",
            "Currently-executing Checkout sagas");

    public static void RecordInitiated() => Initiated.Add(1);

    public static void RecordConfirmed(TimeSpan totalDuration)
    {
        Confirmed.Add(1);
        TotalDuration.Record(totalDuration.TotalMilliseconds,
            new KeyValuePair<string, object?>("saga.outcome", "confirmed"));
    }

    public static void RecordFailed(string errorCode, TimeSpan totalDuration)
    {
        Failed.Add(1, new KeyValuePair<string, object?>(SagaActivityTags.ErrorCode, errorCode));
        TotalDuration.Record(totalDuration.TotalMilliseconds,
            new KeyValuePair<string, object?>("saga.outcome", "failed"),
            new KeyValuePair<string, object?>(SagaActivityTags.ErrorCode, errorCode));
    }

    public static void RecordCompensated(string errorCode, TimeSpan totalDuration, TimeSpan compensationDuration)
    {
        Compensated.Add(1, new KeyValuePair<string, object?>(SagaActivityTags.ErrorCode, errorCode));
        TotalDuration.Record(totalDuration.TotalMilliseconds,
            new KeyValuePair<string, object?>("saga.outcome", "compensated"),
            new KeyValuePair<string, object?>(SagaActivityTags.ErrorCode, errorCode));
        CompensationDuration.Record(compensationDuration.TotalMilliseconds);
    }

    public static void RecordStuck(string lastState, string errorCode)
    {
        Stuck.Add(1,
            new KeyValuePair<string, object?>(CheckoutSagaActivityTags.LastState, lastState),
            new KeyValuePair<string, object?>(SagaActivityTags.ErrorCode, errorCode));
    }

    public static void RecordStockReservationFailed(string reason) =>
        StockReservationFailed.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static void RecordPaymentFailed(string reason) =>
        PaymentFailed.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static void RecordOrderCreationDuration(TimeSpan duration) =>
        OrderCreationDuration.Record(duration.TotalMilliseconds);

    public static void RecordStockReservationDuration(TimeSpan duration) =>
        StockReservationDuration.Record(duration.TotalMilliseconds);

    public static void RecordPaymentDuration(TimeSpan duration) =>
        PaymentDuration.Record(duration.TotalMilliseconds);

    public static void RecordConfirmationDuration(TimeSpan duration) =>
        ConfirmationDuration.Record(duration.TotalMilliseconds);

    public static void IncrementActive() => Active.Add(1);

    public static void DecrementActive() => Active.Add(-1);

    public static void RecordOrderCreationTimeout() => OrderCreationTimeout.Add(1);

    public static void RecordStockReservationTimeout() => StockReservationTimeout.Add(1);

    public static void RecordPaymentTimeout() => PaymentTimeout.Add(1);

    public static void RecordConfirmationTimeout() => ConfirmationTimeout.Add(1);

    public static void RecordCompensationTimeout(string lastState) =>
        CompensationTimeout.Add(1,
            new KeyValuePair<string, object?>(CheckoutSagaActivityTags.LastState, lastState));
}
