using System.Diagnostics;
using System.Diagnostics.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;

namespace DotNetAtlas.Sagas.Common.Observability.Metrics;

/// <summary>
/// OpenTelemetry instrumentation for subscription saga activities.
/// Provides metrics for both purchase and extension sagas with saga type differentiation.
/// </summary>
public static class AlertSubscriptionSagaMetrics
{
    public const string SagaTypePurchase = "purchase";
    public const string SagaTypeExtension = "extension";

    private static readonly Meter Meter = new(ApplicationInfo.AppName, ApplicationInfo.Version);

    // Counters
    private static readonly Counter<long> SagasStarted =
        Meter.CreateCounter<long>("saga.subscriptions.started", "count", "Number of subscription sagas started");

    private static readonly Counter<long> SagasCompleted =
        Meter.CreateCounter<long>("saga.subscriptions.completed", "count",
            "Number of subscription sagas completed successfully");

    private static readonly Counter<long> SagasFailed =
        Meter.CreateCounter<long>("saga.subscriptions.failed", "count", "Number of subscription sagas that failed");

    private static readonly Counter<long> SagasTimedOut =
        Meter.CreateCounter<long>("saga.subscriptions.timedout", "count",
            "Number of subscription sagas that timed out");

    private static readonly Counter<long> CompensationsCompleted =
        Meter.CreateCounter<long>("saga.subscriptions.compensations.completed", "count",
            "Number of compensations completed");

    private static readonly Counter<long> CompensationsTimedOut =
        Meter.CreateCounter<long>("saga.subscriptions.compensations.timedout", "count",
            "Number of compensations that timed out");

    private static readonly Counter<long> PaymentsCompleted =
        Meter.CreateCounter<long>("saga.subscriptions.payments.completed", "count",
            "Number of payments completed successfully");

    private static readonly Counter<long> PaymentsFailed =
        Meter.CreateCounter<long>("saga.subscriptions.payments.failed", "count",
            "Number of payments that failed");

    private static readonly Counter<long> PaymentsTimedOut =
        Meter.CreateCounter<long>("saga.subscriptions.payments.timedout", "count",
            "Number of payments that timed out");

    // Histograms
    private static readonly Histogram<double> SagaDuration =
        Meter.CreateHistogram<double>("saga.subscriptions.duration", "ms", "Duration of subscription sagas");

    private static readonly Histogram<double> PaymentDuration =
        Meter.CreateHistogram<double>("saga.subscriptions.payments.duration", "ms", "Duration of payment processing");

    /// <summary>
    /// Records that a new subscription saga has started.
    /// </summary>
    /// <param name="tier">The subscription tier.</param>
    /// <param name="sagaType">The saga type: "purchase" or "extension".</param>
    public static void RecordSagaStarted(string tier, string sagaType)
    {
        SagasStarted.Add(1,
            new KeyValuePair<string, object?>("saga.subscription_tier", tier),
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));
    }

    /// <summary>
    /// Records that a subscription saga completed successfully.
    /// </summary>
    /// <param name="duration">The duration of the saga.</param>
    /// <param name="sagaType">The saga type: "purchase" or "extension".</param>
    public static void RecordSagaCompleted(TimeSpan duration, string sagaType)
    {
        SagasCompleted.Add(1,
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));

        SagaDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("saga.outcome", "completed"),
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));
    }

    /// <summary>
    /// Records that a subscription saga failed.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="duration">The duration of the saga.</param>
    /// <param name="sagaType">The saga type: "purchase" or "extension".</param>
    public static void RecordSagaFailed(string errorCode, TimeSpan duration, string sagaType)
    {
        SagasFailed.Add(1,
            new KeyValuePair<string, object?>(SagaActivityTags.ErrorCode, errorCode),
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));

        SagaDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("saga.outcome", "failed"),
            new KeyValuePair<string, object?>(SagaActivityTags.ErrorCode, errorCode),
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));
    }

    /// <summary>
    /// Records that a subscription saga timed out.
    /// </summary>
    /// <param name="duration">The duration of the saga.</param>
    /// <param name="sagaType">The saga type: "purchase" or "extension".</param>
    public static void RecordSagaTimeout(TimeSpan duration, string sagaType)
    {
        SagasTimedOut.Add(1,
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));

        SagaDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("saga.outcome", "timeout"),
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));
    }

    /// <summary>
    /// Records that compensation was completed.
    /// </summary>
    /// <param name="duration">The duration of compensation.</param>
    /// <param name="sagaType">The saga type: "purchase" or "extension".</param>
    public static void RecordCompensationCompleted(TimeSpan duration, string sagaType)
    {
        _ = duration; // Used for future metrics tracking
        CompensationsCompleted.Add(1,
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));
    }

    /// <summary>
    /// Records that compensation timed out.
    /// </summary>
    /// <param name="duration">The duration before timeout.</param>
    /// <param name="sagaType">The saga type: "purchase" or "extension".</param>
    public static void RecordCompensationTimeout(TimeSpan duration, string sagaType)
    {
        CompensationsTimedOut.Add(1,
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));

        SagaDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("saga.outcome", "compensation_timeout"),
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));
    }

    /// <summary>
    /// Records that payment completed successfully.
    /// </summary>
    /// <param name="duration">The duration of payment processing.</param>
    /// <param name="sagaType">The saga type: "purchase" or "extension".</param>
    public static void RecordPaymentCompleted(TimeSpan duration, string sagaType)
    {
        PaymentsCompleted.Add(1,
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));

        PaymentDuration.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("saga.payment_outcome", "completed"),
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));
    }

    /// <summary>
    /// Records that payment failed.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="sagaType">The saga type: "purchase" or "extension".</param>
    public static void RecordPaymentFailed(string errorCode, string sagaType)
    {
        PaymentsFailed.Add(1,
            new KeyValuePair<string, object?>(SagaActivityTags.ErrorCode, errorCode),
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));
    }

    /// <summary>
    /// Records that payment timed out.
    /// </summary>
    /// <param name="sagaType">The saga type: "purchase" or "extension".</param>
    public static void RecordPaymentTimeout(string sagaType)
    {
        PaymentsTimedOut.Add(1,
            new KeyValuePair<string, object?>(SagaActivityTags.Type, sagaType));
    }

    /// <summary>
    /// Creates a new activity for a subscription saga operation.
    /// </summary>
    /// <param name="operationName">The name of the operation being traced.</param>
    /// <param name="correlationId">The saga correlation ID.</param>
    /// <param name="sagaType">The saga type: "purchase" or "extension".</param>
    /// <returns>The created activity, or null if tracing is disabled.</returns>
    public static Activity? StartActivity(string operationName, Guid correlationId, string sagaType)
    {
        var activity = SagaActivitySource.ActivitySource.StartActivity(operationName);
        activity?.SetTag(SagaActivityTags.Type, sagaType);
        activity?.SetTag(SagaActivityTags.CorrelationId, correlationId.ToString());
        return activity;
    }
}
