using Ordering.Orders;

namespace Platform.OutboxRelay.WorkerService.IntegrationTests;

/// <summary>
/// <c>OutboxMessage.TopicName</c> is the relay's only routing input — it reads no configuration and
/// has no topic of its own.
/// </summary>
[Collection<OutboxPublishPathTestCollection>]
public sealed class OutboxTopicRoutingTests(OutboxPublishPathFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Two rows in one batch, asserted as two positives. Writing one row and then watching the other
    /// topic stay empty would pass just as well against a relay that published nothing at all.
    /// </summary>
    [Fact]
    public async Task RowsWithDifferentTopicNames_EachArriveOnTheirOwnTopic()
    {
        // Arrange — identical record type on both rows, so TopicName is the only thing that differs.
        var routedToA = NewEvent();
        var routedToB = NewEvent();

        var topicA = await fixture.CreateTopicAsync("route-a");
        var topicB = await fixture.CreateTopicAsync("route-b");

        using var consumerA = fixture.CreateConsumer<OrderDeliveredEvent>(topicA);
        using var consumerB = fixture.CreateConsumer<OrderDeliveredEvent>(topicB);

        // Act
        await fixture.WriteOutboxRowsAsync(
            [
                new OutboxRow(topicA, routedToA.OrderId.ToString(), routedToA),
                new OutboxRow(topicB, routedToB.OrderId.ToString(), routedToB),
            ],
            Ct);

        // Assert
        var receivedOnA = consumerA.ConsumeOne(OutboxPublishPathFixture.AssertionTimeout, Ct);
        var receivedOnB = consumerB.ConsumeOne(OutboxPublishPathFixture.AssertionTimeout, Ct);

        // Outside the scope: a scope collects and continues, so a null here would die on the next
        // line's dereference rather than reporting which topic went quiet.
        receivedOnA.Should().NotBeNull("topic A received nothing");
        receivedOnB.Should().NotBeNull("topic B received nothing");

        using (new AssertionScope())
        {
            receivedOnA!.OrderId.Should().Be(routedToA.OrderId,
                "a relay that routed by anything other than the row's own TopicName would land B's event here");

            receivedOnB!.OrderId.Should().Be(routedToB.OrderId,
                "both rows share a batch, a record type and a schema subject, so only TopicName can separate them");
        }
    }

    private static OrderDeliveredEvent NewEvent() =>
        new()
        {
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            DeliveredAtUtc = new DateTime(2026, 3, 14, 15, 9, 26, 535, DateTimeKind.Utc),
        };
}
