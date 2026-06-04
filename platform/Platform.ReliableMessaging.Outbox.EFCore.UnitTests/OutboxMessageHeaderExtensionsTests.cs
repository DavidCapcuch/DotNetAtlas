using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Platform.ReliableMessaging.Outbox.Core;

namespace Platform.ReliableMessaging.Outbox.EFCore.UnitTests;

/// <summary>
/// Pins OpenTelemetry W3C Trace Context propagation across the outbox boundary. After ADR-0030
/// retired the dedicated correlation id, <c>traceparent</c> is the cross-process correlation key
/// the outbox must carry: the relay's <c>BuildKafkaHeaders</c> copies the row's serialized headers
/// verbatim onto the produced Kafka message, so a <c>traceparent</c> on the row stitches the trace
/// end-to-end (HTTP → outbox → Kafka → consumer).
/// </summary>
public sealed class OutboxMessageHeaderExtensionsTests
{
    [Fact]
    public void BuildOtelHeadersFromActivity_WhenActivityActive_InjectsW3CTraceparentCarryingTheTraceId()
    {
        // Arrange — install the W3C TraceContext propagator the OTel SDK wires in the running host
        // (a bare unit process has only the Noop propagator). Save/restore keeps the global clean.
        var originalPropagator = Propagators.DefaultTextMapPropagator;
        Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());
        try
        {
            // An ambient sampled Activity — the outbox writer runs inside the producing span.
            using var source = new ActivitySource("Platform.ReliableMessaging.Outbox.EFCore.UnitTests");
            using var listener = CreateAllDataListener();
            ActivitySource.AddActivityListener(listener);
            using var activity = source.StartActivity("outbox.write")!;

            // Act
            var headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(activity);

            // Assert — the W3C traceparent must be present and carry the ambient trace id so the
            // relay-produced Kafka message continues the same trace (the only cross-process
            // correlation key post-ADR-0030).
            using (new AssertionScope())
            {
                headers.Should().NotBeNull();
                headers.Should().ContainKey("traceparent",
                    "ADR-0030 keeps W3C Trace Context as the cross-process correlation key; the outbox row must carry traceparent so the relay stitches the trace onto the Kafka message");
                headers!["traceparent"].Should().Contain(activity.TraceId.ToHexString(),
                    "the injected traceparent must carry the ambient trace id end-to-end");
            }
        }
        finally
        {
            Sdk.SetDefaultTextMapPropagator(originalPropagator);
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

    private static ActivityListener CreateAllDataListener() =>
        new()
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
}
