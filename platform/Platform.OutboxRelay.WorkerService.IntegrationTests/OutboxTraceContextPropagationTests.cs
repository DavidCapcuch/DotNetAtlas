using System.Diagnostics;
using Ordering.Orders;

namespace Platform.OutboxRelay.WorkerService.IntegrationTests;

/// <summary>
/// The outbox is a process boundary: the request that wrote the row is long gone by the time the
/// relay publishes it. <c>OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity</c> persists
/// the trace context onto the row, and that is the only thing keeping the consumer in the same
/// trace as the write.
/// </summary>
[Collection<OutboxPublishPathTestCollection>]
public sealed class OutboxTraceContextPropagationTests(OutboxPublishPathFixture fixture)
{
    private const string TraceParentHeader = "traceparent";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RowWrittenInsideAnActivity_ArrivesInThatTrace_UnderTheRelaysOwnProduceSpan()
    {
        // Arrange
        using var source = new ActivitySource($"{nameof(OutboxTraceContextPropagationTests)}.Writer");

        var integrationEvent = new OrderDeliveredEvent
        {
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            DeliveredAtUtc = new DateTime(2026, 3, 14, 15, 9, 26, 535, DateTimeKind.Utc),
        };

        var topic = await fixture.CreateTopicAsync("trace");
        using var consumer = fixture.CreateConsumer<OrderDeliveredEvent>(topic);

        ActivityTraceId writeTraceId;
        ActivitySpanId writeSpanId;

        // Act
        using (var writeActivity = source.StartActivity("outbox.write"))
        {
            writeActivity.Should().NotBeNull(
                "the fixture installs a process-wide ActivityListener; with no sampled Activity the writer stamps no traceparent and this test would prove nothing");

            writeTraceId = writeActivity!.TraceId;
            writeSpanId = writeActivity.SpanId;

            await fixture.WriteOutboxRowsAsync(
                [new OutboxRow(topic, integrationEvent.OrderId.ToString(), integrationEvent)],
                Ct);
        }

        // Assert
        var received = consumer.ConsumeOneResult(OutboxPublishPathFixture.AssertionTimeout, Ct);
        received.Should().NotBeNull();

        var traceParent = received!.ReadHeader(TraceParentHeader);
        traceParent.Should().NotBeNull(
            $"the relay must put a W3C {TraceParentHeader} on the wire, or the consumer starts a brand new trace");

        ActivityContext.TryParse(traceParent, traceState: null, out var wireContext)
            .Should().BeTrue("a traceparent the consumer cannot parse is the same as none at all");

        using (new AssertionScope())
        {
            wireContext.TraceId.Should().Be(writeTraceId,
                "the trace id is what survives the outbox boundary; losing it splits one business operation into two unrelated traces");

            wireContext.SpanId.Should().NotBe(writeSpanId,
                "the relay parents its produce span on the row's traceparent and stamps its own span on the wire — passing the row's headers through unchanged would leave the consumer a sibling of the write rather than a child of the produce, and the relay hop untimeable");
        }
    }
}
