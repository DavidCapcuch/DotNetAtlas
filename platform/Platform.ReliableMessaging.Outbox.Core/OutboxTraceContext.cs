using OpenTelemetry.Context.Propagation;

namespace Platform.ReliableMessaging.Outbox.Core;

/// <summary>
/// The single definition of the trace-context format carried by outbox rows.
/// </summary>
/// <remarks>
/// <para>
/// Pinned rather than read from <c>Propagators.DefaultTextMapPropagator</c>. A row is written by one
/// service, read back later by the relay, and consumed by a third process, all deployed
/// independently — so the format is a contract between them, not a per-host setting. Reading the
/// ambient propagator would make correctness depend on every host being configured identically,
/// which nothing declares or checks, and a row written under one setting would become unreadable
/// under another with no error.
/// </para>
/// <para>
/// Being constructed rather than captured also means no type-initialization order can leave this as
/// the Noop propagator, which silently drops <c>traceparent</c> for a process lifetime.
/// </para>
/// <para>
/// This is the composite the OpenTelemetry SDK installs by default, so it matches what
/// ambient-propagator consumers (KafkaFlow's OpenTelemetry instrumentation) read today. Changing
/// the process propagator will NOT change this format: a deployment that needs a different one must
/// change this field, and must change it for every service writing to the same topics.
/// </para>
/// </remarks>
public static class OutboxTraceContext
{
    /// <summary>
    /// Injects and extracts W3C Trace Context (<c>traceparent</c>, <c>tracestate</c>) plus
    /// <c>baggage</c> on outbox rows and the Kafka messages the relay produces from them.
    /// </summary>
    public static TextMapPropagator Propagator { get; } =
        new CompositeTextMapPropagator([new TraceContextPropagator(), new BaggagePropagator()]);
}
