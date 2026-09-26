using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Notifications.Domain.Deliveries;
using Notifications.Infrastructure.Persistence.Database;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.Test.Framework.Assertions;

namespace Notifications.IntegrationTests.Common;

/// <summary>
/// The persisted outbox row is the proof an event committed; the fixture's <c>FakeOutboxWriter</c>
/// persists it with an empty payload, so the event's fields come from the writer's capture instead.
/// </summary>
internal static class DeliveryRecords
{
    private const string NotifyEventsTopic = "notifications.notify-events";

    /// <summary>The <c>(NotificationId, Channel)</c> ledger row's status, or null when none was recorded.</summary>
    public static async Task<DeliveryStatus?> LoadLedgerStatusAsync(
        this IntegrationTestFixture fixture, Guid notificationId, ChannelType channel, CancellationToken ct)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.NotificationDeliveries
            .Where(d => d.NotificationId == notificationId && d.Channel == channel)
            .Select(d => (DeliveryStatus?)d.Status)
            .SingleOrDefaultAsync(ct);
    }

    public static async Task<List<OutboxMessage>> LoadOutboxRowsAsync(
        this IntegrationTestFixture fixture, CancellationToken ct)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.OutboxMessages.AsNoTracking().ToListAsync(ct);
    }

    /// <summary>
    /// Asserts the delivery events committed for <paramref name="dispatch"/>, one per expected status in
    /// any order, and that the capture holds exactly those events — a captured event with no persisted
    /// row would be one that never committed.
    /// </summary>
    public static async Task AssertCommittedDeliveryEventsAsync(
        this IntegrationTestFixture fixture,
        ChannelType channel,
        NotificationDispatch dispatch,
        IReadOnlyCollection<NotificationDeliveryStatus> expectedStatuses,
        CancellationToken ct)
    {
        var rows = await fixture.LoadOutboxRowsAsync(ct);
        var events = fixture.OutboxWriter.GetMessages<NotificationDeliveryStatusChangedEvent>().ToList();

        using (new AssertionScope())
        {
            rows.Should().HaveCount(expectedStatuses.Count, "each delivery event commits with its ledger row");
            rows.Should().AllSatisfy(row =>
            {
                row.TopicName.Should().Be(NotifyEventsTopic);
                row.KafkaKey.Should().Be(dispatch.RecipientUserId.ToString());
                row.Type.Should().BeMessageType<NotificationDeliveryStatusChangedEvent>();
            });

            events.Select(e => e.IntegrationEvent.Status).Should().BeEquivalentTo(
                expectedStatuses, "the capture must hold only events that committed");
            events.Should().AllSatisfy(e =>
            {
                e.IntegrationEvent.NotificationId.Should().Be(dispatch.NotificationId);
                e.IntegrationEvent.RecipientUserId.Should().Be(dispatch.RecipientUserId);
                e.IntegrationEvent.TemplateKey.Should().Be(dispatch.TemplateKey);
                e.IntegrationEvent.Channel.Should().Be(channel.Name);
            });
        }
    }
}
