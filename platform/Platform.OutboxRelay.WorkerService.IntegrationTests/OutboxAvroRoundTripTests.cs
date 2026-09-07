using Ordering.Orders;
using Platform.Messaging.Abstractions;
using Platform.Test.Framework.Common;

namespace Platform.OutboxRelay.WorkerService.IntegrationTests;

/// <summary>
/// The publish path end to end: a real <c>IOutboxWriter</c> serializes against a live Schema
/// Registry, the running relay lifts the row onto Kafka, and a real consumer deserializes it.
/// </summary>
/// <remarks>
/// The relay itself never touches Avro — it produces <c>OutboxMessage.AvroPayload</c> verbatim as
/// bytes. So what this proves is the writer's serialization, the consumer's deserialization against
/// the same registry, and that the relay carries the payload through unaltered.
/// </remarks>
[Collection<OutboxPublishPathTestCollection>]
public sealed class OutboxAvroRoundTripTests(OutboxPublishPathFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WrittenIntegrationEvent_ArrivesOnItsTopic_AndDeserializesToAnEqualRecord()
    {
        // Arrange — a fixed millisecond-precision instant. The field is Avro timestamp-millis, so a
        // DateTime.UtcNow expectation would carry sub-millisecond ticks the round trip discards and
        // the assertion would fail on a correct publish.
        var expected = new OrderDeliveredEvent
        {
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            DeliveredAtUtc = new DateTime(2026, 3, 14, 15, 9, 26, 535, DateTimeKind.Utc),
        };

        var topic = await fixture.CreateTopicAsync("round-trip");
        using var consumer = fixture.CreateConsumer<OrderDeliveredEvent>(topic);

        // Act
        var writtenIds = await fixture.WriteOutboxRowsAsync(
            [new OutboxRow(topic, expected.OrderId.ToString(), expected)],
            Ct);

        // Assert
        var received = consumer.ConsumeOne(OutboxPublishPathFixture.AssertionTimeout, Ct);

        // Outside the scope: a scope collects and continues, so a null here would die on the next
        // line's dereference and discard this message — which is the most likely failure in the suite.
        received.Should().NotBeNull(
            "the relay must lift the written row onto its topic; nothing arriving means the publish path is broken, not slow");

        using (new AssertionScope())
        {
            received!.OrderId.Should().Be(expected.OrderId);
            received.BuyerId.Should().Be(expected.BuyerId);
            received.DeliveredAtUtc.Should().Be(expected.DeliveredAtUtc,
                "a payload the relay altered, or a schema the consumer resolved to a different version, would surface here as a changed field rather than a failed consume");
        }

        await Eventually.UntilAsync(
            async token => !await fixture.AnyOutboxRowsRemainAsync(writtenIds, token),
            OutboxPublishPathFixture.AssertionTimeout,
            $"outbox row {writtenIds[0]} to be deleted, which is how the relay records a delivery the broker acknowledged",
            Ct);
    }

    /// <summary>
    /// The row's envelope — its Kafka key and its persisted headers — is carried by
    /// <c>OutboxMessageRelay.BuildKafkaHeaders</c>, which nothing else observes. Without this test
    /// that method can return an empty <c>Headers</c> and every other test here stays green, because
    /// the produce span's <c>traceparent</c> is injected into the message separately.
    /// </summary>
    [Fact]
    public async Task WrittenRow_CarriesItsKafkaKeyAndOutboxHeaders_OntoTheWire()
    {
        // Arrange
        var integrationEvent = new OrderDeliveredEvent
        {
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            DeliveredAtUtc = new DateTime(2026, 3, 14, 15, 9, 26, 535, DateTimeKind.Utc),
        };

        var topic = await fixture.CreateTopicAsync("envelope");
        using var consumer = fixture.CreateConsumer<OrderDeliveredEvent>(topic);

        // Act
        await fixture.WriteOutboxRowsAsync(
            [new OutboxRow(topic, integrationEvent.OrderId.ToString(), integrationEvent)],
            Ct);

        // Assert
        var received = consumer.ConsumeOneResult(OutboxPublishPathFixture.AssertionTimeout, Ct);
        received.Should().NotBeNull();

        using (new AssertionScope())
        {
            received!.Message.Key.Should().Be(integrationEvent.OrderId.ToString(),
                "the key is the per-aggregate ordering guarantee across the topic's partitions, so dropping it silently reorders one aggregate's events");

            var messageId = received.ReadHeader(MessageHeaderKeys.MessageId);
            Guid.TryParse(messageId, out _).Should().BeTrue(
                "consumers dedupe on message.id — InboxMiddleware throws without it, so losing it here breaks every consumer in production while this suite stays green");

            received.ReadHeader(MessageHeaderKeys.Origin).Should().Be(OutboxPublishPathFixture.MessageOrigin,
                "origin names the service that produced the event and is the only provenance a consumer gets");
        }
    }
}
