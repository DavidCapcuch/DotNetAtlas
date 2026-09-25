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
using Xunit;

namespace Notifications.IntegrationTests.Dispatch;

/// <summary>
/// The email channel (ADR-0032 § 2) entered through its durable dispatch job, against a real
/// <see cref="NotificationsDbContext"/> and the Mailpit testcontainer. The ledger is asserted against
/// the real DB; the delivery event on the fixture's outbox substitute (no Schema Registry stood up).
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class EmailChannelDispatcherTests : BaseIntegrationTest
{
    private const string NotifyEventsTopic = "notifications.notify-events";

    public EmailChannelDispatcherTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Dispatch_RendersSubjectAndBodyFromDbTemplate_SendsToMailpit_RecordsDispatchedLedger_AndEmitsDispatchedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeInvoiceTemplateAsync(ct);
        // Another user's row, minted and inserted FIRST, so it is also what an unfiltered lookup
        // returns (insertion order and UUIDv7 key order agree) — the recipient assertion below then
        // fails if the resolver ever stops filtering by recipient. Seeded second, it would be masked.
        await ArrangePreferenceAsync(Guid.CreateVersion7(), email: "someone-else@dotnetatlas.test", ct);
        var notificationId = Guid.CreateVersion7();
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, email: "invoice-buyer@dotnetatlas.test", ct);
        var dispatch = BuildDispatch(notificationId, recipientUserId);

        await Fixture.RunDispatchJobAsync(ChannelType.Email, dispatch, ct);

        var messages = await Fixture.Mailpit.GetMessagesAsync(ct);
        messages.Should().ContainSingle();

        // Folded from DbRecipientResolverTests: the address is the DB-backed resolver's output,
        // observed where production consumes it.
        messages[0].To.Should().ContainSingle().Which.Address.Should().Be("invoice-buyer@dotnetatlas.test");

        // Subject rendered from template_channels.subject + payload ({{InvoiceNumber}} → value).
        messages[0].Subject.Should().Be("Invoice INV-2026-000042 — your copy is ready");

        // Body rendered from template_channels.body + payload (every {{token}} substituted).
        var detail = await Fixture.Mailpit.GetMessageAsync(messages[0].Id, ct);
        detail.Text.Should().Contain("Your invoice INV-2026-000042 is ready.");
        detail.Text.Should().Contain("Total: 152.00 EUR");
        detail.Text.Should().Contain("00000000-0000-0000-0000-000000000001");

        (await LoadLedgerStatusAsync(notificationId, ct)).Should().Be(DeliveryStatus.Dispatched);

        Fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
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
        await ArrangePreferenceAsync(recipientUserId, email: "buyer@dotnetatlas.test", ct);
        var dispatch = BuildDispatch(notificationId, recipientUserId);

        await Fixture.RunDispatchJobAsync(ChannelType.Email, dispatch, ct);
        await Fixture.RunDispatchJobAsync(ChannelType.Email, dispatch, ct); // ledger already Dispatched → skip

        var messages = await Fixture.Mailpit.GetMessagesAsync(ct);
        messages.Should().ContainSingle("the second dispatch must skip on the Dispatched ledger row");

        Fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
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
        await ArrangePreferenceAsync(recipientUserId, email: "buyer@dotnetatlas.test", ct);
        var dispatch = BuildDispatch(notificationId, recipientUserId);

        // Constructed directly rather than through the job: this case needs a gateway that fails once
        // then succeeds, and the fixture has no seam to inject a per-test IEmailGateway into the host.
        // The first attempt records Failed and rethrows a retryable EmailDispatchFailedException — a
        // transient send failure is not bug-class.
        var gateway = new SequencedEmailGateway(Result.Fail("smtp down"), Result.Ok());
        await using (var scope = Fixture.CreateScope())
        {
            var dispatcher = BuildDispatcher(scope, gateway);
            await Assert.ThrowsAsync<EmailDispatchFailedException>(() => dispatcher.DispatchAsync(dispatch, ct));
        }

        (await LoadLedgerStatusAsync(notificationId, ct)).Should().Be(DeliveryStatus.Failed);

        // Retry in a fresh scope (as a Hangfire retry would): same row UPDATEs to Dispatched —
        // a second INSERT on the (NotificationId, Channel) key would throw a unique violation.
        await using (var scope = Fixture.CreateScope())
        {
            var dispatcher = BuildDispatcher(scope, gateway);
            await dispatcher.DispatchAsync(dispatch, ct);
        }

        await using (var verifyScope = Fixture.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var rows = await db.NotificationDeliveries
                .Where(d => d.NotificationId == notificationId && d.Channel == ChannelType.Email)
                .ToListAsync(ct);
            rows.Should().ContainSingle("the retry must UPDATE the row, never INSERT a second one");
            rows[0].Status.Should().Be(DeliveryStatus.Dispatched);
        }

        Fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
            NotifyEventsTopic,
            Arg.Any<string>(),
            Arg.Is<NotificationDeliveryStatusChangedEvent>(e => e.Status == NotificationDeliveryStatus.Failed));
        Fixture.OutboxSubstitute.Received(1).AddOutboxMessage(
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

        await Fixture.AssertDispatchJobFailsAsync(
            ChannelType.Email, dispatch, "Notifications.MissingEmailTemplateChannel", ct);

        (await Fixture.Mailpit.GetMessagesAsync(ct)).Should().BeEmpty("a missing template must fail before sending");
        Fixture.OutboxSubstitute.DidNotReceive().AddOutboxMessage(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NotificationDeliveryStatusChangedEvent>());
    }

    [Fact]
    public async Task Dispatch_EmailTemplateChannelHasNoSubject_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeSubjectlessEmailTemplateAsync(ct);
        var dispatch = BuildDispatch(Guid.CreateVersion7(), Guid.CreateVersion7());

        // Email requires a subject; a null-subject Email template channel is a misconfigured template.
        await Fixture.AssertDispatchJobFailsAsync(
            ChannelType.Email, dispatch, "Notifications.EmailTemplateMissingSubject", ct);

        (await Fixture.Mailpit.GetMessagesAsync(ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_RecipientHasNoPreferenceRow_Throws_AndSendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        // Folded from DbRecipientResolverTests: the resolver loud-fails on a missing user_preferences
        // row. The template is arranged so the dispatcher reaches recipient resolution.
        await ArrangeInvoiceTemplateAsync(ct);
        var dispatch = BuildDispatch(Guid.CreateVersion7(), Guid.CreateVersion7());

        await Fixture.AssertDispatchJobFailsAsync(
            ChannelType.Email, dispatch, "Notifications.MissingRecipientPreference", ct);

        (await Fixture.Mailpit.GetMessagesAsync(ct)).Should().BeEmpty("an unresolvable recipient must fail before sending");
        Fixture.OutboxSubstitute.DidNotReceive().AddOutboxMessage(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<NotificationDeliveryStatusChangedEvent>());
    }

    [Fact]
    public async Task Dispatch_PayloadMissingTemplateToken_Throws_AndSendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeInvoiceTemplateAsync(ct);
        // A preference row exists so the dispatcher reaches the unresolved-token guard (rather than
        // loud-failing earlier on a missing recipient address).
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, email: "buyer@dotnetatlas.test", ct);
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

        var exception = await Fixture.AssertDispatchJobFailsAsync(
            ChannelType.Email, dispatch, "Notifications.UnresolvedTemplateTokens", ct);

        exception.Message.Should().Contain("ViewInvoiceUrl");
        (await Fixture.Mailpit.GetMessagesAsync(ct)).Should().BeEmpty("an incomplete payload must fail before sending");
        Fixture.OutboxSubstitute.DidNotReceive().AddOutboxMessage(
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

    private EmailChannelDispatcher BuildDispatcher(AsyncServiceScope scope, IEmailGateway gateway)
    {
        var sp = scope.ServiceProvider;
        return new EmailChannelDispatcher(
            sp.GetRequiredService<INotificationsDbContext>(),
            Fixture.OutboxSubstitute,
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
        await using var scope = Fixture.CreateScope();
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
        await using var scope = Fixture.CreateScope();
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
        await using var scope = Fixture.CreateScope();
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
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var row = await db.NotificationDeliveries.SingleAsync(
            d => d.NotificationId == notificationId && d.Channel == ChannelType.Email, ct);
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
