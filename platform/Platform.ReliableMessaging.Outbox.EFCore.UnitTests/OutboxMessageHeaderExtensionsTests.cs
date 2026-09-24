using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Platform.ReliableMessaging.Outbox.Core;

namespace Platform.ReliableMessaging.Outbox.EFCore.UnitTests;

/// <summary>
/// Pins OpenTelemetry W3C Trace Context propagation across the outbox boundary. <c>traceparent</c>
/// is the cross-process correlation key the outbox must carry: the relay parents its produce span on
/// the row's <c>traceparent</c> and stamps that span onto the Kafka message, so a <c>traceparent</c>
/// on the row is what keeps the trace one chain (HTTP → outbox → relay produce → consumer).
/// </summary>
public sealed class OutboxMessageHeaderExtensionsTests
{
    [Fact]
    public void BuildOtelHeadersFromActivity_WhenActivityActive_InjectsW3CTraceparentCarryingTheTraceId()
    {
        // Arrange — no propagator is installed, which is a bare unit process's starting state and
        // also a host that never calls AddOpenTelemetry(). The row format does not depend on it.
        using var source = new ActivitySource("Platform.ReliableMessaging.Outbox.EFCore.UnitTests");
        using var listener = CreateAllDataListener();
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("outbox.write")!;

        // Act
        var headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(activity);

        // Assert
        using (new AssertionScope())
        {
            headers.Should().NotBeNull();
            headers.Should().ContainKey("traceparent",
                "W3C Trace Context is the cross-process correlation key; the outbox row must carry traceparent so the relay stitches the trace onto the Kafka message");
            headers!["traceparent"].Should().Contain(activity.TraceId.ToHexString(),
                "the injected traceparent must carry the ambient trace id end-to-end");
        }
    }

    [Fact]
    public void BuildOtelHeadersFromActivity_WhenTheProcessPropagatorIsReplaced_StillWritesW3CTraceparent()
    {
        // Arrange — the outbox row is persisted and read back by the relay in another process, so
        // its header format is a storage contract rather than a per-host setting. Changing the
        // process propagator must not change what lands in the column.
        var originalPropagator = Propagators.DefaultTextMapPropagator;
        try
        {
            Sdk.SetDefaultTextMapPropagator(new SentinelPropagator());

            using var source = new ActivitySource("Platform.ReliableMessaging.Outbox.EFCore.UnitTests");
            using var listener = CreateAllDataListener();
            ActivitySource.AddActivityListener(listener);
            using var activity = source.StartActivity("outbox.write")!;

            // Act
            var headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(activity);

            // Assert
            using (new AssertionScope())
            {
                headers.Should().ContainKey("traceparent");
                headers.Should().NotContainKey(SentinelPropagator.HeaderKey,
                    "reading the ambient propagator here would make the persisted row format follow a per-host setting, and would reopen the type-initialization race that froze it as Noop");
            }
        }
        finally
        {
            Sdk.SetDefaultTextMapPropagator(originalPropagator);
        }
    }

    [Fact]
    public void BuildOtelHeadersFromActivity_WithAmbientBaggage_CarriesItOnTheRow()
    {
        // Arrange — baggage is the documented channel for custom context across the outbox
        // (Outbox.EFCore README), so it is half of the pinned row format, not an optional extra.
        var originalBaggage = Baggage.Current;
        try
        {
            using var source = new ActivitySource("Platform.ReliableMessaging.Outbox.EFCore.UnitTests");
            using var listener = CreateAllDataListener();
            ActivitySource.AddActivityListener(listener);
            using var activity = source.StartActivity("outbox.write")!;

            Baggage.SetBaggage("tenant", "acme");

            // Act
            var headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(activity);

            // Assert
            headers.Should().ContainKey("baggage").WhoseValue.Should().Contain("tenant=acme",
                "dropping the baggage propagator from the pinned format would silently stop carrying custom context the README tells callers to rely on");
        }
        finally
        {
            Baggage.Current = originalBaggage;
        }
    }

    [Fact]
    public void BuildOtelHeadersFromActivity_WithBaggageNeedingEncoding_SurvivesTheColumnRoundTrip()
    {
        // Arrange — baggage values arrive from callers (ASP.NET Core hydrates Baggage.Current from
        // the inbound `baggage` header), so the row format has to carry characters the W3C Baggage
        // spec requires percent-encoding. The encoder belongs to the OpenTelemetry package and
        // changes under us on a bump, while the column it writes is read back by a separately
        // deployed relay — so what needs pinning is that a value survives write → column → read.
        var originalBaggage = Baggage.Current;
        const string valueNeedingEncoding = "acme corp, s.r.o. ü";
        try
        {
            using var source = new ActivitySource("Platform.ReliableMessaging.Outbox.EFCore.UnitTests");
            using var listener = CreateAllDataListener();
            ActivitySource.AddActivityListener(listener);
            using var activity = source.StartActivity("outbox.write")!;

            Baggage.SetBaggage("tenant", valueNeedingEncoding);

            // Act — the whole persisted path: inject, serialize into the column, read the column
            // back, extract through the same pinned propagator the relay uses.
            var headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(activity)!;
            var row = new OutboxMessage
            {
                TopicName = "platform.test",
                AvroPayload = [],
                Type = "TestEvent",
                CreatedUtc = DateTimeOffset.UnixEpoch,
                Headers = OutboxMessageHeaderExtensions.SerializeHeaders(headers),
            };
            var fromColumn = row.DeserializeHeaders()!;
            var extracted = OutboxTraceContext.Propagator.Extract(default, fromColumn, ReadHeader);

            // Assert
            using (new AssertionScope())
            {
                extracted.Baggage.GetBaggage("tenant").Should().Be(valueNeedingEncoding,
                    "the relay reads the row in another process, so a value the writer put in baggage has to come back unchanged or custom context is silently corrupted");
                row.Headers.Should().NotBeNull().And.NotContain(valueNeedingEncoding,
                    "an unencoded value in the column is not W3C-conformant, and a consumer parsing it per spec would split it at the comma");
            }
        }
        finally
        {
            Baggage.Current = originalBaggage;
        }
    }

    [Fact]
    public void SerializeHeaders_PreservesTraceparent_SoTheOutboxRowCarriesItToTheRelay()
    {
        // Arrange — the trace context as it would sit on a freshly built outbox-row header set.
        var headers = new Dictionary<string, string>
        {
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        };

        // Act
        var json = OutboxMessageHeaderExtensions.SerializeHeaders(headers);

        // Assert — the serialized column value the relay reads back must retain the trace context.
        json.Should().NotBeNull();
        json.Should().Contain("traceparent")
            .And.Contain("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
    }

    [Fact]
    public void BuildOtelHeadersFromActivity_WhenActivityIsNull_ReturnsNull()
    {
        // Arrange — OutboxWriter calls this when no Activity is active; contract is to return null
        // (the writer falls through to an empty header set).
        Activity.Current.Should().BeNull("the test does not start an Activity");

        // Act
        var headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(null);

        // Assert
        headers.Should().BeNull();
    }

    private static IEnumerable<string>? ReadHeader(Dictionary<string, string> headers, string key) =>
        headers.TryGetValue(key, out var value) ? [value] : null;

    private static ActivityListener CreateAllDataListener() =>
        new()
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };

    /// <summary>
    /// Injects one recognisable key and nothing else, so a test can tell whether a call site reached
    /// the process propagator rather than inferring it from an absence.
    /// </summary>
    private sealed class SentinelPropagator : TextMapPropagator
    {
        public const string HeaderKey = "x-sentinel-propagator";

        public override ISet<string> Fields => new HashSet<string> { HeaderKey };

        public override void Inject<T>(
            PropagationContext context, T carrier, Action<T, string, string> setter) =>
            setter(carrier, HeaderKey, "1");

        public override PropagationContext Extract<T>(
            PropagationContext context, T carrier, Func<T, string, IEnumerable<string>?> getter) =>
            context;
    }
}
