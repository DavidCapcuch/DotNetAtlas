using Ordering.Orders;
using Platform.Test.Framework.Common;

namespace Platform.OutboxRelay.WorkerService.IntegrationTests;

/// <summary>
/// What the relay does when rows become visible in a different order than their ids were assigned.
/// </summary>
/// <remarks>
/// Postgres stamps the outbox id at INSERT but the row stays invisible until COMMIT, so a row can
/// appear <i>below</i> ids the relay has already published. The contract that has to survive that
/// is in <c>docs/bc-design/conventions.md</c> § 6: the late row is published, one poll behind.
/// The arrival order <i>is</i> asserted, because it catches a commit leaking out of the held-write
/// seam. What is deliberately not claimed is that the reordering is a <i>guarantee</i> — it is a
/// consequence of publishing in insert order, and this repo promises no order between the two.
/// </remarks>
[Collection<OutboxPublishPathTestCollection>]
public sealed class OutboxCommitOrderTests(OutboxPublishPathFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A row that becomes visible after higher ids were published and cleared must still be
    /// selected and published, never skipped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the regression guard for the relay holding no record of what it published. Any
    /// resumption mark — an <c>Id &gt; lastSent</c> filter, however cached — makes this row
    /// permanently invisible to the relay: it is below the mark, so it is never selected again,
    /// and nothing anywhere reports the event as lost. Verified by mutation: reintroducing that
    /// filter fails this test on the second <c>ConsumeOne</c>.
    /// </para>
    /// <para>
    /// <b>What this does not cover.</b> The range-delete hazard that
    /// <c>OutboxMessageRelay</c>'s delete comment warns about is a different instant — it needs the
    /// row to commit between the relay's SELECT and its DELETE, so that the DELETE's snapshot sees
    /// a row the SELECT did not. Here the row is still uncommitted when the delete runs, and is
    /// therefore invisible to it, so widening the delete to <c>WHERE id &lt;= max</c> leaves this
    /// test passing. Postgres-level tricks cannot open that window — a <c>BEFORE DELETE</c> trigger
    /// fires after the statement snapshot is taken, and a blocked row lock resumes without
    /// rescanning — but decorating the relay's Kafka producer to commit on first
    /// <c>Produce</c> would land inside it. Until something does, the inline comment on the delete
    /// is that hazard's only guard.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task WhenALowerIdRowBecomesVisibleAfterHigherIdsWerePublished_TheRelayStillPublishesIt()
    {
        // Arrange — one key, so both rows share a partition and nothing here depends on
        // cross-partition behaviour.
        var topic = await fixture.CreateTopicAsync("commit-order");
        using var consumer = fixture.CreateConsumer<OrderDeliveredEvent>(topic);

        var lateCommit = NewEvent();
        var earlyCommit = NewEvent();
        var sharedKey = Guid.NewGuid().ToString();

        // The lower id, written first and held uncommitted: invisible to the relay's SELECT.
        await using var pending = await fixture.BeginOutboxWriteAsync(
            [new OutboxRow(topic, sharedKey, lateCommit)],
            Ct);

        // Act — the higher id commits first, so the relay publishes and clears it while the lower
        // id is still in flight. That is what puts the lower id behind the relay's progress.
        var committedIds = await fixture.WriteOutboxRowsAsync(
            [new OutboxRow(topic, sharedKey, earlyCommit)],
            Ct);

        pending.Ids[0].Should().BeLessThan(committedIds[0],
            "the held write must hold the lower id, or this exercises nothing");

        var first = consumer.ConsumeOne(OutboxPublishPathFixture.AssertionTimeout, Ct);
        first.Should().NotBeNull("the committed row must publish while the lower id is still uncommitted");
        first!.OrderId.Should().Be(earlyCommit.OrderId);

        await Eventually.UntilAsync(
            async token => !await fixture.AnyOutboxRowsRemainAsync(committedIds, token),
            OutboxPublishPathFixture.AssertionTimeout,
            $"outbox row {committedIds[0]} to be published and cleared, so the relay has moved past that id",
            Ct);

        await pending.CommitAsync(Ct);

        // Assert — the row now visible below the relay's last published id is still picked up.
        var second = consumer.ConsumeOne(OutboxPublishPathFixture.AssertionTimeout, Ct);
        second.Should().NotBeNull(
            "a row appearing below an already-published id must still be selected; nothing arriving means the relay kept a resumption mark and skipped it permanently");
        second!.OrderId.Should().Be(lateCommit.OrderId,
            "the message arriving second must be the late-committing row, not a redelivery of the first");

        await Eventually.UntilAsync(
            async token => !await fixture.AnyOutboxRowsRemainAsync(pending.Ids, token),
            OutboxPublishPathFixture.AssertionTimeout,
            $"outbox row {pending.Ids[0]} to be deleted once the broker acknowledged it",
            Ct);
    }

    private static OrderDeliveredEvent NewEvent() =>
        new()
        {
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            DeliveredAtUtc = new DateTime(2026, 3, 14, 15, 9, 26, 535, DateTimeKind.Utc),
        };
}
