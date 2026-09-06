using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Platform.OutboxRelay.WorkerService.Observability.Tracing;

namespace Platform.OutboxRelay.WorkerService.UnitTests;

/// <summary>
/// Pins the relay's half of the outbox trace path: it continues the trace the producing service
/// started, and hands the consumer a parent that names the relay's own produce span, so a trace can
/// be walked HTTP → outbox row → relay produce → consumer without a broken link.
/// </summary>
public sealed class KafkaProducerDiagnosticsTests
{
    private const string RowTraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string RowSpanId = "b7ad6b7169203331";

    [Fact]
    public void StartProduceActivity_WithARowTraceparent_ParentsTheProduceSpanOnIt()
    {
        // Arrange — no propagator is installed, which is a bare unit process's starting state and
        // also a host that never calls AddOpenTelemetry(). Reading the row must not depend on it.
        using var listener = CreateRecordingListener();
        ActivitySource.AddActivityListener(listener);

        // Act
        using var activity = StartProduceActivity(NewKafkaMessage());

        // Assert
        using (new AssertionScope())
        {
            activity.Should().NotBeNull();
            activity!.TraceId.ToHexString().Should().Be(RowTraceId,
                "the produce span must continue the producing service's trace rather than starting a new one");
            activity.ParentSpanId.ToHexString().Should().Be(RowSpanId,
                "the row's span must become the produce span's parent, not merely share its trace id");
        }
    }

    [Fact]
    public void StartProduceActivity_StampsTheOutgoingMessageWithTheProduceSpan()
    {
        // Arrange — the message arrives carrying the row's headers verbatim, which name the
        // producing service's span.
        using var listener = CreateRecordingListener();
        ActivitySource.AddActivityListener(listener);

        var message = NewKafkaMessage();

        // Act
        using var activity = StartProduceActivity(message);

        // Assert — the consumer must become a child of the relay's produce span. Leaving the row's
        // traceparent on the wire makes the consumer a sibling instead, so the produce span has no
        // descendants and "did the relay send this, and how long did the hop take" is unanswerable.
        using (new AssertionScope())
        {
            activity.Should().NotBeNull();
            OutgoingTraceparent(message).Should().Be(
                $"00-{activity!.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-01",
                "the produced message must name the produce span as the consumer's parent");
            OutgoingTraceparent(message).Should().NotContain(RowSpanId,
                "the row's span id is the producing service's, one hop further up");
            message.Headers.Count(header => header.Key == "traceparent").Should().Be(1,
                "Kafka permits duplicate keys, so appending rather than replacing would put two traceparent values on the wire and let the consumer pick either");
        }
    }

    [Fact]
    public void StartProduceActivity_WhenTheProcessPropagatorIsReplaced_StillReadsW3CRowHeaders()
    {
        // Arrange — the relay reads rows written earlier by other processes, so the format it parses
        // is fixed by what OutboxMessageHeaderExtensions writes, not by this host's setting.
        var originalPropagator = Propagators.DefaultTextMapPropagator;
        try
        {
            Sdk.SetDefaultTextMapPropagator(new FixedContextPropagator());

            using var listener = CreateRecordingListener();
            ActivitySource.AddActivityListener(listener);

            var message = NewKafkaMessage();

            // Act
            using var activity = StartProduceActivity(message);

            // Assert — both directions under a replaced propagator: the row is still parsed as W3C,
            // and the outgoing stamp is still written as W3C. FixedContextPropagator injects nothing,
            // so if either site read the ambient propagator the message would keep the row's
            // traceparent — which is what makes this kill the mutant whatever the ambient state is.
            using (new AssertionScope())
            {
                activity.Should().NotBeNull();
                activity!.TraceId.ToHexString().Should().Be(RowTraceId,
                    "reading the ambient propagator here would make the relay's parsing follow a per-host setting, and would reopen the type-initialization race that froze it as Noop");
                OutgoingTraceparent(message).Should().Be(
                    $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-01",
                    "the outgoing stamp must be W3C regardless of what propagator this host has installed");
            }
        }
        finally
        {
            Sdk.SetDefaultTextMapPropagator(originalPropagator);
        }
    }

    private static Dictionary<string, string> RowHeaders() =>
        new() { ["traceparent"] = $"00-{RowTraceId}-{RowSpanId}-01" };

    /// <summary>
    /// A message shaped the way the relay builds one — the row's headers copied verbatim, before any
    /// produce span exists (mirrors <c>OutboxMessageRelay.BuildKafkaHeaders</c>).
    /// </summary>
    private static Message<string?, byte[]> NewKafkaMessage()
    {
        var headers = new Headers();
        foreach (var (key, value) in RowHeaders())
        {
            headers.Add(key, Encoding.UTF8.GetBytes(value));
        }

        return new Message<string?, byte[]> { Key = "order-1", Value = [1, 2, 3], Headers = headers };
    }

    private static string? OutgoingTraceparent(Message<string?, byte[]> message) =>
        message.Headers.TryGetLastBytes("traceparent", out var bytes)
            ? Encoding.UTF8.GetString(bytes)
            : null;

    private static Activity? StartProduceActivity(Message<string?, byte[]> message) =>
        KafkaProducerDiagnostics.StartProduceActivityAndStampTraceContext("orders.events", message, RowHeaders());

    /// <summary>
    /// Records AND sets the sampled flag, mirroring a host whose upstream trace was sampled — which
    /// is what puts <c>-01</c> on the outgoing <c>traceparent</c>. Plain <c>AllData</c> records
    /// without the flag and would emit <c>-00</c>.
    /// </summary>
    private static ActivityListener CreateRecordingListener() =>
        new()
        {
            ShouldListenTo = source => source.Name == OutboxRelayActivitySource.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };

    /// <summary>
    /// Extracts one recognisable trace context and ignores the carrier, so a test can tell whether a
    /// call site reached the process propagator rather than inferring it from an absence.
    /// </summary>
    private sealed class FixedContextPropagator : TextMapPropagator
    {
        private const string TraceId = "11111111111111111111111111111111";
        private const string SpanId = "2222222222222222";

        public override ISet<string> Fields => new HashSet<string>();

        public override PropagationContext Extract<T>(
            PropagationContext context, T carrier, Func<T, string, IEnumerable<string>?> getter) =>
            new(
                new ActivityContext(
                    ActivityTraceId.CreateFromString(TraceId),
                    ActivitySpanId.CreateFromString(SpanId),
                    ActivityTraceFlags.Recorded),
                context.Baggage);

        public override void Inject<T>(
            PropagationContext context, T carrier, Action<T, string, string> setter)
        {
            // Extract-only stub; the relay's produce path stamps headers through OutboxTraceContext.
        }
    }
}
