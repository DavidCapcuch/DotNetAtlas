using System.Diagnostics.Metrics;

namespace Inventory.Infrastructure.Observability;

/// <summary>
/// OpenTelemetry instrumentation for Inventory infrastructure-layer observability —
/// currently the event-store rehydration histograms required by ADR-0006 § Observability.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0006 mandates two histograms emitted around the
/// <c>EventStoreRepository.RehydrateAsync</c> SELECT-and-fold path so the v2-snapshot
/// trigger has a real signal:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>inventory.aggregate.rehydration.duration</c> (ms) — alert threshold is
///     <c>p99 &gt; 1s</c> over a 15-minute window per ADR-0006:217. Set well below the
///     saga's <c>StockReservationSeconds: 60</c> ceiling so the cliff is detected long
///     before it interacts with saga timeouts.
///   </item>
///   <item>
///     <c>inventory.aggregate.rehydration.event_count</c> ({events}) — same tagging so
///     high-latency rehydrations can be cross-correlated with stream length to confirm
///     the cliff is O(N) and not a network blip.
///   </item>
/// </list>
/// <para>
/// Both histograms are tagged by <c>product_id</c> (the stream id) — a hot SKU's slow
/// rehydration is the canonical signal for the snapshot work item.
/// </para>
/// <para>
/// Pattern A (static <c>Meter</c> + static instruments) per the
/// <c>PaymentProcessingSagaMetrics</c> precedent at
/// <c>saga/SagaOrchestrators/Common/Observability/Metrics/PaymentProcessingSagaMetrics.cs:14</c>.
/// </para>
/// </remarks>
internal static class InventoryMetrics
{
    /// <summary>
    /// Meter name, exposed for OpenTelemetry registration (e.g.
    /// <c>WithMetrics(metrics =&gt; metrics.AddMeter(InventoryMetrics.MeterName))</c> in a
    /// future <c>ObservabilityDependencyInjection</c>; current Inventory wiring relies on the
    /// integration tests' direct <see cref="MeterListener"/> subscription, mirroring sibling
    /// BCs that have not yet wired per-service OTel either).
    /// </summary>
    public const string MeterName = "Inventory";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> RehydrationDurationMs = Meter.CreateHistogram<double>(
        name: "inventory.aggregate.rehydration.duration",
        unit: "ms",
        description: "EventStoreRepository.RehydrateAsync duration tagged by product_id (ADR-0006 § Observability).");

    private static readonly Histogram<long> RehydrationEventCount = Meter.CreateHistogram<long>(
        name: "inventory.aggregate.rehydration.event_count",
        unit: "{events}",
        description: "Event count folded per RehydrateAsync call tagged by product_id (ADR-0006 § Observability).");

    /// <summary>
    /// Records one rehydration measurement against both histograms. Tag is
    /// <c>product_id</c> — matches the stream id since Inventory uses one stream per
    /// product per <c>inventory.md</c> § 8. Empty-stream rehydrates record (durationMs ≈ 0,
    /// eventCount = 0) — that's intentional per ADR-0006 (cliff detection cross-correlates
    /// with stream length).
    /// </summary>
    public static void RecordRehydration(Guid productId, double durationMs, int eventCount)
    {
        var tag = new KeyValuePair<string, object?>("product_id", productId);
        RehydrationDurationMs.Record(durationMs, tag);
        RehydrationEventCount.Record(eventCount, tag);
    }
}
