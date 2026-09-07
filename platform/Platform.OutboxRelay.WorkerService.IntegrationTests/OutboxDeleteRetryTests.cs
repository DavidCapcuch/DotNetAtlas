using Microsoft.Extensions.DependencyInjection;
using Ordering.Orders;
using Platform.OutboxRelay.WorkerService.Observability.Metrics;
using Platform.Test.Framework.Common;

namespace Platform.OutboxRelay.WorkerService.IntegrationTests;

/// <summary>
/// What the relay does when a batch cannot be cleared: which rows it deletes, which it keeps, and
/// whether it still claims success.
/// </summary>
[Collection<OutboxPublishPathTestCollection>]
public sealed class OutboxDeleteRetryTests(OutboxPublishPathFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A failed DELETE leaves the row in place, so the poll loop republishes it and retries the
    /// delete — and the relay must not report success while it cannot drain.
    /// </summary>
    [Fact]
    public async Task WhenTheDeleteFails_TheRowIsRepublished_AndTheRelayReportsNoSuccess()
    {
        // Arrange
        var topic = await fixture.CreateTopicAsync("delete-retry");
        using var consumer = fixture.CreateConsumer<OrderDeliveredEvent>(topic);

        var metrics = fixture.Services.GetRequiredService<OutboxRelayMetrics>();
        var integrationEvent = NewEvent();

        await fixture.BlockOutboxDeletesAsync(Ct);
        IReadOnlyList<long> writtenIds;
        try
        {
            // Act — delivered, but the row cannot be removed.
            writtenIds = await fixture.WriteOutboxRowsAsync(
                [new OutboxRow(topic, integrationEvent.OrderId.ToString(), integrationEvent)],
                Ct);

            var first = consumer.ConsumeOne(OutboxPublishPathFixture.AssertionTimeout, Ct);
            first.Should().NotBeNull("a delete that fails must not stop the message being published");

            (await fixture.AnyOutboxRowsRemainAsync(writtenIds, Ct)).Should().BeTrue(
                "a row the relay could not delete must stay in the table rather than being forgotten");

            var lastSuccessWhileStuck = metrics.LastSuccessfulExecution;

            // The row is still there, so the next poll selects it again — which is what retries the
            // delete. Seeing the same event twice is the retry, not a defect.
            var republished = consumer.ConsumeOne(OutboxPublishPathFixture.AssertionTimeout, Ct);
            republished.Should().NotBeNull(
                "the undeleted row must be selected again on a later poll; nothing arriving means the relay stopped retrying it");
            republished!.OrderId.Should().Be(integrationEvent.OrderId,
                "the republished message must be the row that could not be deleted");

            metrics.LastSuccessfulExecution.Should().Be(lastSuccessWhileStuck,
                "a poll that cannot clear its delete must not be stamped successful, or the health check reports Healthy while the outbox is stuck — which is the failure this makes visible");
        }
        finally
        {
            await fixture.AllowOutboxDeletesAsync(Ct);
        }

        // Assert — the retry clears the row without a restart.
        await Eventually.UntilAsync(
            async token => !await fixture.AnyOutboxRowsRemainAsync(writtenIds, token),
            OutboxPublishPathFixture.AssertionTimeout,
            $"outbox row {writtenIds[0]} to be deleted once deletes are allowed again",
            Ct);
    }

    /// <summary>
    /// A row whose delivery failed must not be deleted, and neither must anything queued behind it.
    /// </summary>
    /// <remarks>
    /// <c>Produce</c> is fire-and-forget, so a row is recorded as produced before its delivery report
    /// arrives — which means the failed row is in the produced set and is excluded only by the
    /// boundary being inclusive. Narrowing it by one deletes the undelivered row unsent.
    /// </remarks>
    [Fact]
    public async Task WhenOneRowInABatchFailsDelivery_ItSurvives_AndOnlyTheDeliveredRowIsDeleted()
    {
        // Arrange — the second row routes to a topic that was never created, and the broker does not
        // auto-create, so its delivery report comes back as a failure.
        var deliverableTopic = await fixture.CreateTopicAsync("partial-failure");
        var missingTopic = $"platform.outbox-tests.never-created-{Guid.NewGuid():N}";

        using var consumer = fixture.CreateConsumer<OrderDeliveredEvent>(deliverableTopic);

        var delivered = NewEvent();
        var undeliverable = NewEvent();

        // Act — one batch, the deliverable row first so it holds the lower id.
        var writtenIds = await fixture.WriteOutboxRowsAsync(
            [
                new OutboxRow(deliverableTopic, delivered.OrderId.ToString(), delivered),
                new OutboxRow(missingTopic, undeliverable.OrderId.ToString(), undeliverable),
            ],
            Ct);

        var deliveredId = writtenIds[0];
        var undeliverableId = writtenIds[1];

        try
        {
            consumer.ConsumeOne(OutboxPublishPathFixture.AssertionTimeout, Ct).Should().NotBeNull(
                "the deliverable row must still publish even though a later row in its batch cannot");

            // Assert
            await Eventually.UntilAsync(
                async token => !await fixture.AnyOutboxRowsRemainAsync([deliveredId], token),
                OutboxPublishPathFixture.AssertionTimeout,
                $"outbox row {deliveredId} to be deleted after the broker acknowledged it",
                Ct);

            (await fixture.AnyOutboxRowsRemainAsync([undeliverableId], Ct)).Should().BeTrue(
                "the row whose delivery failed must stay in the table to be retried; deleting it would lose the event with no error anywhere");
        }
        finally
        {
            // The relay would otherwise re-select this row on every poll for the rest of the run,
            // and being the lowest id it takes each batch's delete set down with it.
            await fixture.DeleteOutboxRowsAsync([undeliverableId], Ct);
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
