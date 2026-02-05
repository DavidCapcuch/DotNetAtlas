using System.Diagnostics;
using System.Diagnostics.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability;

namespace DotNetAtlas.Sagas.Common.Observability.Metrics;

/// <summary>
/// OpenTelemetry instrumentation for payment saga activities.
/// Provides metrics specific to the payment processing saga.
/// </summary>
public static class PaymentProcessingSagaInstrumentation
{
    private static readonly Meter Meter = new(SagaActivitySource.MeterName, ApplicationInfo.Version);

    // Counters
    private static readonly Counter<long> SagasStarted =
        Meter.CreateCounter<long>("saga.payments.started", "count", "Number of payment sagas started");

    private static readonly Counter<long> AuthorizationsCompleted =
        Meter.CreateCounter<long>("saga.payments.authorizations.completed", "count",
            "Number of payment authorizations completed");

    private static readonly Counter<long> AuthorizationsFailed =
        Meter.CreateCounter<long>("saga.payments.authorizations.failed", "count",
            "Number of payment authorizations that failed");

    private static readonly Counter<long> CapturesCompleted =
        Meter.CreateCounter<long>("saga.payments.captures.completed", "count",
            "Number of payment captures completed");

    private static readonly Counter<long> CapturesFailed =
        Meter.CreateCounter<long>("saga.payments.captures.failed", "count",
            "Number of payment captures that failed");

    private static readonly Counter<long> VoidsCompleted =
        Meter.CreateCounter<long>("saga.payments.voids.completed", "count",
            "Number of payment voids completed");

    private static readonly Counter<long> RefundsCompleted =
        Meter.CreateCounter<long>("saga.payments.refunds.completed", "count",
            "Number of payment refunds completed");

    private static readonly Counter<long> RefundsRequested =
        Meter.CreateCounter<long>("saga.payments.refunds.requested", "count",
            "Number of payment refunds requested");

    private static readonly Counter<long> SagasCompleted =
        Meter.CreateCounter<long>("saga.payments.completed", "count",
            "Number of payment sagas completed successfully");

    private static readonly Counter<long> SagasTimedOut =
        Meter.CreateCounter<long>("saga.payments.timedout", "count",
            "Number of payment sagas that timed out");

    // Histograms
    private static readonly Histogram<double> SagaDuration =
        Meter.CreateHistogram<double>("saga.payments.duration", "ms", "Duration of payment sagas");

    /// <summary>
    /// Records that a new payment saga has started.
    /// This is a "dumb" payment saga - only records payment-specific telemetry, no business context.
    /// </summary>
    public static void RecordSagaStarted(string currency)
    {
        SagasStarted.Add(1,
            new KeyValuePair<string, object?>(PaymentSagaActivityTags.Currency, currency));
    }

    /// <summary>
    /// Records that payment authorization completed.
    /// </summary>
    public static void RecordAuthorizationCompleted()
    {
        AuthorizationsCompleted.Add(1);
    }

    /// <summary>
    /// Records that payment authorization failed.
    /// </summary>
    public static void RecordAuthorizationFailed(string errorCode)
    {
        AuthorizationsFailed.Add(1,
            new KeyValuePair<string, object?>(SagaActivityTags.ErrorCode, errorCode));
    }

    /// <summary>
    /// Records that payment capture completed.
    /// </summary>
    public static void RecordCaptureCompleted()
    {
        CapturesCompleted.Add(1);
    }

    /// <summary>
    /// Records that payment capture failed.
    /// </summary>
    public static void RecordCaptureFailed(string errorCode)
    {
        CapturesFailed.Add(1,
            new KeyValuePair<string, object?>(SagaActivityTags.ErrorCode, errorCode));
    }

    /// <summary>
    /// Records that payment void completed.
    /// </summary>
    public static void RecordVoidCompleted()
    {
        VoidsCompleted.Add(1);
    }

    /// <summary>
    /// Records that payment refund completed.
    /// </summary>
    public static void RecordRefundCompleted()
    {
        RefundsCompleted.Add(1);
    }

    /// <summary>
    /// Records that a refund was requested.
    /// </summary>
    public static void RecordRefundRequested()
    {
        RefundsRequested.Add(1);
    }

    /// <summary>
    /// Records that a payment saga completed successfully.
    /// </summary>
    public static void RecordSagaCompleted(TimeSpan duration)
    {
        SagasCompleted.Add(1);
        SagaDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("saga.outcome", "completed"));
    }

    /// <summary>
    /// Records that a payment saga timed out.
    /// </summary>
    public static void RecordSagaTimeout(string stage, TimeSpan duration)
    {
        SagasTimedOut.Add(1,
            new KeyValuePair<string, object?>(PaymentSagaActivityTags.TimeoutStage, stage));
        SagaDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("saga.outcome", "timeout"),
            new KeyValuePair<string, object?>(PaymentSagaActivityTags.TimeoutStage, stage));
    }

    /// <summary>
    /// Creates a new activity for a payment saga operation.
    /// </summary>
    public static Activity? StartActivity(string operationName, Guid correlationId)
    {
        var activity = SagaActivitySource.ActivitySource.StartActivity(operationName);
        activity?.SetTag(SagaActivityTags.Type, "payment");
        activity?.SetTag(SagaActivityTags.CorrelationId, correlationId.ToString());
        return activity;
    }
}
