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
using Notifications.Domain.Preferences;
using Notifications.Domain.Templates;
using Notifications.Infrastructure.Dispatch;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.IntegrationTests.Common;
using NSubstitute;
using Platform.SharedKernel.Exceptions;
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
    public async Task Dispatch_RendersSubjectAndBodyFromDbTemplate_SendsToMailpit_RecordsDispatchedLedger_AndEmitsDispatchedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeInvoiceTemplateAsync(ct);
        var notificationId = Guid.CreateVersion7();
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, "invoice-buyer@dotnetatlas.test", ct);
        var dispatch = BuildDispatch(notificationId, recipientUserId);

        await using (var scope = _fixture.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Email);
            await dispatcher.DispatchAsync(dispatch, ct);
        }

        var messages = await _fixture.Mailpit.GetMessagesAsync(ct);
        messages.Should().ContainSingle();

        // The recipient address is resolved from user_preferences.email (#314 — replaces the synthetic stub).
        messages[0].To.Should().ContainSingle().Which.Address.Should().Be("invoice-buyer@dotnetatlas.test");

        // Subject rendered from template_channels.subject + payload ({{InvoiceNumber}} → value).
        messages[0].Subject.Should().Be("Invoice INV-2026-000042 — your copy is ready");

        // Body rendered from template_channels.body + payload (every {{token}} substituted).
        var detail = await _fixture.Mailpit.GetMessageAsync(messages[0].Id, ct);
        detail.Text.Should().Contain("Your invoice INV-2026-000042 is ready.");
        detail.Text.Should().Contain("Total: 152.00 EUR");
        detail.Text.Should().Contain("00000000-0000-0000-0000-000000000001");

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
        await ArrangeInvoiceTemplateAsync(ct);
        var notificationId = Guid.CreateVersion7();
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, "buyer@dotnetatlas.test", ct);
        var dispatch = BuildDispatch(notificationId, recipientUserId);

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
        await ArrangeInvoiceTemplateAsync(ct);
        var notificationId = Guid.CreateVersion7();
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, "buyer@dotnetatlas.test", ct);
        var dispatch = BuildDispatch(notificationId, recipientUserId);

        // First attempt: gateway fails → records Failed and rethrows a (retryable) EmailDispatchFailedException
        // so Hangfire would retry — a transient send failure is NOT bug-class.
        var gateway = new SequencedEmailGateway(Result.Fail("smtp down"), Result.Ok());
        await using (var scope = _fixture.CreateScope())
        {
            var dispatcher = BuildDispatcher(scope, gateway);
            await Assert.ThrowsAsync<EmailDispatchFailedException>(() => dispatcher.DispatchAsync(dispatch, ct));
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

    [Fact]
    public async Task Dispatch_NoEmailTemplateChannel_Throws_AndSendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        // Deliberately arrange nothing — the (TemplateKey, Email) row is absent (producer named an
        // unknown template). The dispatcher must fail before sending or writing the outbox.
        var dispatch = BuildDispatch(Guid.CreateVersion7(), Guid.CreateVersion7());

        await using (var scope = _fixture.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Email);
            await Assert.ThrowsAsync<DataIntegrityException>(() => dispatcher.DispatchAsync(dispatch, ct));
        }

        (await _fixture.Mailpit.GetMessagesAsync(ct)).Should().BeEmpty("a missing template must fail before sending");
        _fixture.OutboxSubstitute.DidNotReceive().AddOutboxMessage(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NotificationDeliveryStatusChangedEvent>());
    }

    [Fact]
    public async Task Dispatch_EmailTemplateChannelHasNoSubject_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeSubjectlessEmailTemplateAsync(ct);
        var dispatch = BuildDispatch(Guid.CreateVersion7(), Guid.CreateVersion7());

        await using var scope = _fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Email);

        // Email requires a subject; a null-subject Email template channel is a misconfigured template.
        await Assert.ThrowsAsync<DataIntegrityException>(() => dispatcher.DispatchAsync(dispatch, ct));
        (await _fixture.Mailpit.GetMessagesAsync(ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_PayloadMissingTemplateToken_Throws_AndSendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeInvoiceTemplateAsync(ct);
        // A preference row exists so the dispatcher reaches the unresolved-token guard (rather than
        // loud-failing earlier on a missing recipient address).
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, "buyer@dotnetatlas.test", ct);
        // Payload omits ViewInvoiceUrl, which the template body references. The dispatcher must
        // loud-fail rather than email a customer a literal "{{ViewInvoiceUrl}}" + record Dispatched.
        var dispatch = new NotificationDispatch
        {
            NotificationId = Guid.CreateVersion7(),
            RecipientUserId = recipientUserId,
            TemplateKey = "invoicing.invoice-delivered",
            Payload = new Dictionary<string, string>
            {
                ["InvoiceNumber"] = "INV-2026-000042",
                ["TotalAmount"] = "152.00",
                ["Currency"] = "EUR",
                // ViewInvoiceUrl intentionally omitted
            },
        };

        await using (var scope = _fixture.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredKeyedService<IChannelDispatcher>(ChannelType.Email);
            await Assert.ThrowsAsync<DataIntegrityException>(() => dispatcher.DispatchAsync(dispatch, ct));
        }

        (await _fixture.Mailpit.GetMessagesAsync(ct)).Should().BeEmpty("an incomplete payload must fail before sending");
        _fixture.OutboxSubstitute.DidNotReceive().AddOutboxMessage(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NotificationDeliveryStatusChangedEvent>());
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
            gateway,
            sp.GetRequiredService<IOptions<TopicsOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<EmailChannelDispatcher>>());
    }

    private async Task ArrangeInvoiceTemplateAsync(CancellationToken ct)
    {
        // Tests arrange their own templates — UseAsyncSeeding does not fire under Evolve migrations
        // (notifications.md § 10). Mirrors the dev seed for invoicing.invoice-delivered → [Email].
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Templates.Add(Template.Create(
            "invoicing.invoice-delivered",
            "Sent to a buyer when their invoice is issued and ready to view."));
        db.TemplateChannels.Add(TemplateChannel.Create(
            "invoicing.invoice-delivered",
            ChannelType.Email,
            subject: "Invoice {{InvoiceNumber}} — your copy is ready",
            body: """
                  Hello,

                  Your invoice {{InvoiceNumber}} is ready.
                  Total: {{TotalAmount}} {{Currency}}
                  Sign in to view & download: {{ViewInvoiceUrl}}
                  """));
        await db.SaveChangesAsync(ct);
    }

    private async Task ArrangeSubjectlessEmailTemplateAsync(CancellationToken ct)
    {
        // A misconfigured Email template: the channel exists but has no subject line.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Templates.Add(Template.Create(
            "invoicing.invoice-delivered",
            "Misconfigured invoice template with no email subject."));
        db.TemplateChannels.Add(TemplateChannel.Create(
            "invoicing.invoice-delivered",
            ChannelType.Email,
            subject: null,
            body: "Your invoice {{InvoiceNumber}} is ready."));
        await db.SaveChangesAsync(ct);
    }

    private async Task ArrangePreferenceAsync(Guid recipientUserId, string email, CancellationToken ct)
    {
        // The DB-backed recipient resolver (#314) reads the address from user_preferences, so every
        // send-path test must seed the recipient's row.
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.UserPreferences.Add(NotificationPreference.Create(
            recipientUserId,
            email,
            phoneNumber: "+420600000000",
            enabledChannels: [ChannelType.Email],
            quietHoursStart: null,
            quietHoursEnd: null,
            timeZone: "Europe/Prague"));
        await db.SaveChangesAsync(ct);
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
