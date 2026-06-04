using AwesomeAssertions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Application.Common.Data;
using Notifications.Application.Common.Messaging;
using Notifications.Application.Dispatch;
using Notifications.Application.Email;
using Notifications.Application.Recipients;
using Notifications.Domain.Channels;
using Notifications.Domain.Deliveries;
using Notifications.Infrastructure.Dispatch;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.IntegrationTests.Common;
using NSubstitute;
using Xunit;

namespace Notifications.IntegrationTests.Dispatch;

/// <summary>
/// Dispatcher-direct integration coverage for the email channel (ADR-0032 § 2): the real
/// <see cref="EmailChannelDispatcher"/> against a real <see cref="NotificationsDbContext"/> + the
/// Mailpit testcontainer, bypassing Hangfire. The ledger is asserted against the real DB; the
/// outbox event is asserted on the fixture's outbox substitute (no Schema Registry stood up).
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class EmailChannelDispatcherTests : BaseIntegrationTest
{
    private const string NotifyEventsTopic = "notifications.notify-events";

    private readonly IntegrationTestFixture _fixture;

    public EmailChannelDispatcherTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
        _fixture.ResetOutboxSubstitute();
    }

    [Fact]
    public async Task Dispatch_SendsEmailToMailpit_RecordsDispatchedLedger_AndEmitsDispatchedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var notificationId = Guid.CreateVersion7();
        var recipientUserId = Guid.CreateVersion7();
        var dispatch = BuildDispatch(notificationId, recipientUserId);

        await using (var scope = _fixture.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Email);
            await dispatcher.DispatchAsync(dispatch, ct);
        }

        var messages = await _fixture.Mailpit.GetMessagesAsync(ct);
        messages.Should().ContainSingle();
        messages[0].Subject.Should().Be("Invoice INV-2026-000042 — your copy is ready");

        (await LoadLedgerStatusAsync(notificationId, ct)).Should().Be(DeliveryStatus.Dispatched);

        _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            NotifyEventsTopic,
            recipientUserId.ToString(),
            Arg.Is<NotificationDeliveryStatusChangedEvent>(e =>
                e.NotificationId == notificationId
                && e.Channel == "Email"
                && e.Status == NotificationDeliveryStatus.Dispatched));
    }

    [Fact]
    public async Task Dispatch_Redelivered_DoesNotSendTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        var notificationId = Guid.CreateVersion7();
        var dispatch = BuildDispatch(notificationId, Guid.CreateVersion7());

        await DispatchViaKeyedAsync(dispatch, ct);
        await DispatchViaKeyedAsync(dispatch, ct); // ledger already Dispatched → skip

        var messages = await _fixture.Mailpit.GetMessagesAsync(ct);
        messages.Should().ContainSingle("the second dispatch must skip on the Dispatched ledger row");

        _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            NotifyEventsTopic,
            Arg.Any<string>(),
            Arg.Any<NotificationDeliveryStatusChangedEvent>());
    }

    [Fact]
    public async Task Dispatch_GatewayFailsThenSucceeds_UpsertsTheSameRowToDispatched()
    {
        var ct = TestContext.Current.CancellationToken;
        var notificationId = Guid.CreateVersion7();
        var dispatch = BuildDispatch(notificationId, Guid.CreateVersion7());

        // First attempt: gateway fails → records Failed and rethrows (so Hangfire would retry).
        var gateway = new SequencedEmailGateway(Result.Fail("smtp down"), Result.Ok());
        await using (var scope = _fixture.CreateScope())
        {
            var dispatcher = BuildDispatcher(scope, gateway);
            await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(dispatch, ct));
        }

        (await LoadLedgerStatusAsync(notificationId, ct)).Should().Be(DeliveryStatus.Failed);

        // Retry in a fresh scope (as a Hangfire retry would): same row UPDATEs to Dispatched —
        // a second INSERT on the (NotificationId, Channel) key would throw a unique violation.
        await using (var scope = _fixture.CreateScope())
        {
            var dispatcher = BuildDispatcher(scope, gateway);
            await dispatcher.DispatchAsync(dispatch, ct);
        }

        await using (var verifyScope = _fixture.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var rows = await db.NotificationDeliveries
                .Where(d => d.NotificationId == notificationId)
                .ToListAsync(ct);
            rows.Should().ContainSingle("the retry must UPDATE the row, never INSERT a second one");
            rows[0].Status.Should().Be(DeliveryStatus.Dispatched);
        }

        _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            NotifyEventsTopic,
            Arg.Any<string>(),
            Arg.Is<NotificationDeliveryStatusChangedEvent>(e => e.Status == NotificationDeliveryStatus.Failed));
        _fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            NotifyEventsTopic,
            Arg.Any<string>(),
            Arg.Is<NotificationDeliveryStatusChangedEvent>(e => e.Status == NotificationDeliveryStatus.Dispatched));
    }

    private static NotificationDispatch BuildDispatch(Guid notificationId, Guid recipientUserId) => new()
    {
        NotificationId = notificationId,
        RecipientUserId = recipientUserId,
        TemplateKey = "invoicing.invoice-delivered",
        Payload = new Dictionary<string, string>
        {
            ["InvoiceNumber"] = "INV-2026-000042",
            ["TotalAmount"] = "152.00",
            ["Currency"] = "EUR",
            ["ViewInvoiceUrl"] = "https://invoicing.example.com/invoices/00000000-0000-0000-0000-000000000001",
        },
    };

    private async Task DispatchViaKeyedAsync(NotificationDispatch dispatch, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Email);
        await dispatcher.DispatchAsync(dispatch, ct);
    }

    private EmailChannelDispatcher BuildDispatcher(AsyncServiceScope scope, IEmailGateway gateway)
    {
        var sp = scope.ServiceProvider;
        return new EmailChannelDispatcher(
            sp.GetRequiredService<INotificationsDbContext>(),
            _fixture.OutboxSubstitute,
            sp.GetRequiredService<IRecipientResolver>(),
            sp.GetRequiredService<IEmailTemplateRenderer>(),
            gateway,
            sp.GetRequiredService<IOptions<TopicsOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<EmailChannelDispatcher>>());
    }

    private async Task<DeliveryStatus> LoadLedgerStatusAsync(Guid notificationId, CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var row = await db.NotificationDeliveries.SingleAsync(d => d.NotificationId == notificationId, ct);
        return row.Status;
    }

    private sealed class SequencedEmailGateway : IEmailGateway
    {
        private readonly Queue<Result> _results;

        public SequencedEmailGateway(params Result[] results)
        {
            _results = new Queue<Result>(results);
        }

        public Task<Result> SendAsync(EmailMessage message, CancellationToken ct)
        {
            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : Result.Ok());
        }
    }
}
