using System.Diagnostics;
using Platform.Messaging.Abstractions;
using Platform.ReliableMessaging.Outbox.Core;

namespace Platform.ReliableMessaging.Outbox.EFCore.UnitTests;

/// <summary>
/// Pins cross-cutting wave1-followup #256: the outbox row's serialised headers carry
/// <c>correlation.id</c> as a top-level key — not just baggage-encoded inside the OTel
/// propagation header. The relay's <c>BuildKafkaHeaders</c> copies whatever is in the
/// outbox row, so this is the producer-side edge of "top-level correlation.id Kafka header".
/// </summary>
public sealed class OutboxMessageHeaderExtensionsCorrelationIdTests
{
    [Fact]
    public void BuildOtelHeadersFromActivity_WhenActivityHasCorrelationIdTag_HeadersContainTopLevelCorrelationId()
    {
        // Arrange — ASP.NET CorrelationIdMiddleware sets the tag on Activity.Current; the outbox
        // writer must propagate that as a top-level Kafka header per ADR-0008 implementation notes.
        using var source = new ActivitySource("Platform.ReliableMessaging.Outbox.EFCore.UnitTests");
        using var listener = CreateAllDataListener();
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("outbox.write")!;
        var expected = Guid.CreateVersion7().ToString();
        activity.SetTag(MessageHeaderKeys.CorrelationId, expected);

        // Act
        var headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(activity);

        // Assert
        using (new AssertionScope())
        {
            headers.Should().NotBeNull();
            headers.Should().ContainKey(MessageHeaderKeys.CorrelationId,
                "#256: top-level 'correlation.id' Kafka header is the canonical shape per ADR-0008; the OutboxMessageRelay's BuildKafkaHeaders copies it onto the produced message verbatim");
            headers![MessageHeaderKeys.CorrelationId].Should().Be(expected,
                "the propagated value must match the ambient Activity tag set at the HTTP edge");
        }
    }

    [Fact]
    public void BuildOtelHeadersFromActivity_WhenActivityHasNoCorrelationIdTag_HeadersOmitTopLevelCorrelationId()
    {
        // Arrange — a background worker may start an Activity without ever populating the tag
        // (no HTTP context, no inbound Kafka header). The helper must not invent a value.
        using var source = new ActivitySource("Platform.ReliableMessaging.Outbox.EFCore.UnitTests");
        using var listener = CreateAllDataListener();
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("outbox.write")!;

        // Act
        var headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(activity);

        // Assert
        // headers may still contain traceparent etc.; the assertion targets correlation.id specifically.
        if (headers is not null)
        {
            headers.Should().NotContainKey(MessageHeaderKeys.CorrelationId,
                "missing tag => missing top-level header (no synthesized value here; the producer middleware does that downstream when it observes the outbox-relayed message)");
        }
    }

    [Fact]
    public void BuildOtelHeadersFromActivity_WhenActivityIsNull_ReturnsNull()
    {
        // Arrange — regression net for the existing contract (used by OutboxWriter when no
        // Activity is active; current behaviour is to fall through to `headers ?? []`).
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
