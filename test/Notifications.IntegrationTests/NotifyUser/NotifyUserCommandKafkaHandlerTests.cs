using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Notifications.Application.Common.Data;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Notifications.Domain.Preferences;
using Notifications.Domain.Templates;
using Notifications.Infrastructure.NotifyUser;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.IntegrationTests.Common;
using NSubstitute;
using Platform.SharedKernel.Exceptions;
using Xunit;

namespace Notifications.IntegrationTests.NotifyUser;

/// <summary>
/// Integration coverage for the channel-resolution fan-out (notifications.md § 5.3) and the
/// per-channel <c>ExecuteAt</c> split-time scheduling (§ 5.4, #315): the real
/// <see cref="NotifyUserCommandKafkaHandler"/> against a real <see cref="NotificationsDbContext"/>,
/// resolving <c>enabled_channels ∩ template_channels</c> over arranged preference + template rows and
/// enqueuing per resolved channel. The Hangfire enqueuer is substituted so the assertions are on which
/// channels were enqueued for which instant, not on a live job runner (which the test host does not start).
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class NotifyUserCommandKafkaHandlerTests : BaseIntegrationTest
{
    private readonly IntegrationTestFixture _fixture;

    public NotifyUserCommandKafkaHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Handle_EnqueuesOneJobPerResolvedChannel()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedTemplateAsync(ct); // supports [Email, Bell, Sms]
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, [ChannelType.Email, ChannelType.Sms, ChannelType.Bell], ct);

        var enqueuer = Substitute.For<IChannelDispatchEnqueuer>();
        var notificationId = Guid.CreateVersion7();

        await HandleAsync(enqueuer, notificationId, recipientUserId, "order.shipped", ct);

        // Every channel both supported by the template and enabled by the recipient is enqueued exactly once,
        // and the dispatch faithfully carries the command's fields (NotificationId, RecipientUserId, TemplateKey, Payload).
        enqueuer.Received(1).Enqueue(
            ChannelType.Email,
            Arg.Is<NotificationDispatch>(d =>
                d.NotificationId == notificationId
                && d.RecipientUserId == recipientUserId
                && d.TemplateKey == "order.shipped"
                && d.Payload["InvoiceNumber"] == "INV-2026-000042"),
            Arg.Any<DateTimeOffset>());
        enqueuer.Received(1).Enqueue(ChannelType.Sms, Arg.Any<NotificationDispatch>(), Arg.Any<DateTimeOffset>());
        enqueuer.Received(1).Enqueue(ChannelType.Bell, Arg.Any<NotificationDispatch>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task Handle_QuietHoursUserInsideWindow_DefersSmsToQuietEnd_AndKeepsOtherChannelsImmediate()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedTemplateAsync(ct);
        var recipientUserId = Guid.CreateVersion7();
        // The seeded quiet-hours shape (notifications.md § 8): 22:00–07:00 Europe/Prague.
        await ArrangePreferenceAsync(
            recipientUserId,
            [ChannelType.Email, ChannelType.Sms, ChannelType.Bell],
            ct,
            quietHoursStart: new TimeOnly(22, 0),
            quietHoursEnd: new TimeOnly(7, 0));

        // 2026-06-09 23:30 CEST = 21:30Z — inside the window; it ends 06-10 07:00 CEST = 05:00Z.
        var now = new DateTimeOffset(2026, 6, 9, 21, 30, 0, TimeSpan.Zero);
        var expectedSmsExecuteAt = new DateTimeOffset(2026, 6, 10, 5, 0, 0, TimeSpan.Zero);
        var enqueuer = Substitute.For<IChannelDispatchEnqueuer>();
        var logger = new CollectingLogger<NotifyUserCommandKafkaHandler>();

        await HandleAsync(
            enqueuer, Guid.CreateVersion7(), recipientUserId, "order.shipped", ct,
            new FakeTimeProvider(now), logger);

        // Only the quiet-hours-respecting channel (Sms) defers; email and bell stay immediate.
        enqueuer.Received(1).Enqueue(ChannelType.Sms, Arg.Any<NotificationDispatch>(), expectedSmsExecuteAt);
        enqueuer.Received(1).Enqueue(ChannelType.Email, Arg.Any<NotificationDispatch>(), now);
        enqueuer.Received(1).Enqueue(ChannelType.Bell, Arg.Any<NotificationDispatch>(), now);

        logger.Messages.Should().ContainSingle(m => m.Contains("deferred", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_UserWithoutQuietHours_EnqueuesAllChannelsImmediately()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedTemplateAsync(ct);
        var recipientUserId = Guid.CreateVersion7();
        // The admin/dev seeded shape: all channels on, no quiet hours.
        await ArrangePreferenceAsync(recipientUserId, [ChannelType.Email, ChannelType.Sms, ChannelType.Bell], ct);

        var now = new DateTimeOffset(2026, 6, 9, 21, 30, 0, TimeSpan.Zero);
        var enqueuer = Substitute.For<IChannelDispatchEnqueuer>();

        await HandleAsync(
            enqueuer, Guid.CreateVersion7(), recipientUserId, "order.shipped", ct, new FakeTimeProvider(now));

        enqueuer.Received(1).Enqueue(ChannelType.Sms, Arg.Any<NotificationDispatch>(), now);
        enqueuer.Received(1).Enqueue(ChannelType.Email, Arg.Any<NotificationDispatch>(), now);
        enqueuer.Received(1).Enqueue(ChannelType.Bell, Arg.Any<NotificationDispatch>(), now);
    }

    [Fact]
    public async Task Handle_SmsDisabledUser_SuppressesSmsByResolution()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeOrderShippedTemplateAsync(ct);
        var recipientUserId = Guid.CreateVersion7();
        // The pleb seeded shape: Sms OFF — the ∩ suppresses a channel the template supports (§ 5.3).
        await ArrangePreferenceAsync(recipientUserId, [ChannelType.Email, ChannelType.Bell], ct);

        var enqueuer = Substitute.For<IChannelDispatchEnqueuer>();

        await HandleAsync(enqueuer, Guid.CreateVersion7(), recipientUserId, "order.shipped", ct);

        enqueuer.DidNotReceive().Enqueue(ChannelType.Sms, Arg.Any<NotificationDispatch>(), Arg.Any<DateTimeOffset>());
        enqueuer.Received(1).Enqueue(ChannelType.Email, Arg.Any<NotificationDispatch>(), Arg.Any<DateTimeOffset>());
        enqueuer.Received(1).Enqueue(ChannelType.Bell, Arg.Any<NotificationDispatch>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task Handle_WhenRecipientDisabledTheChannel_EnqueuesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeInvoiceTemplateAsync(ct); // supports [Email] only
        var recipientUserId = Guid.CreateVersion7();
        // Email disabled — only Sms/Bell enabled, neither supported by the invoice template.
        await ArrangePreferenceAsync(recipientUserId, [ChannelType.Sms, ChannelType.Bell], ct);

        var enqueuer = Substitute.For<IChannelDispatchEnqueuer>();

        await HandleAsync(enqueuer, Guid.CreateVersion7(), recipientUserId, "invoicing.invoice-delivered", ct);

        enqueuer.DidNotReceive().Enqueue(
            Arg.Any<ChannelType>(), Arg.Any<NotificationDispatch>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task Handle_WhenRecipientHasNoPreferenceRow_EnqueuesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeInvoiceTemplateAsync(ct);
        // No preference row arranged for this recipient → no enabled channels → empty resolution.
        var enqueuer = Substitute.For<IChannelDispatchEnqueuer>();

        await HandleAsync(enqueuer, Guid.CreateVersion7(), Guid.CreateVersion7(), "invoicing.invoice-delivered", ct);

        enqueuer.DidNotReceive().Enqueue(
            Arg.Any<ChannelType>(), Arg.Any<NotificationDispatch>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task Handle_WhenTemplateHasNoChannels_ThrowsAndEnqueuesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        // Deliberately arrange nothing — the producer named a template with no channel rows (unknown template).
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, [ChannelType.Email], ct);
        var enqueuer = Substitute.For<IChannelDispatchEnqueuer>();

        var act = () => HandleAsync(enqueuer, Guid.CreateVersion7(), recipientUserId, "unknown.template", ct);

        await act.Should().ThrowAsync<DataIntegrityException>();
        enqueuer.DidNotReceive().Enqueue(
            Arg.Any<ChannelType>(), Arg.Any<NotificationDispatch>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task Handle_WhenEnqueueFails_PropagatesSoTheInboxRollsBack()
    {
        var ct = TestContext.Current.CancellationToken;
        await ArrangeInvoiceTemplateAsync(ct);
        var recipientUserId = Guid.CreateVersion7();
        await ArrangePreferenceAsync(recipientUserId, [ChannelType.Email], ct);

        var enqueuer = Substitute.For<IChannelDispatchEnqueuer>();
        enqueuer
            .When(e => e.Enqueue(Arg.Any<ChannelType>(), Arg.Any<NotificationDispatch>(), Arg.Any<DateTimeOffset>()))
            .Do(_ => throw new InvalidOperationException("scheduler down"));

        var act = () => HandleAsync(enqueuer, Guid.CreateVersion7(), recipientUserId, "invoicing.invoice-delivered", ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task HandleAsync(
        IChannelDispatchEnqueuer enqueuer,
        Guid notificationId,
        Guid recipientUserId,
        string templateKey,
        CancellationToken ct,
        TimeProvider? clock = null,
        ILogger<NotifyUserCommandKafkaHandler>? logger = null)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<INotificationsDbContext>();
        var handler = new NotifyUserCommandKafkaHandler(
            db,
            enqueuer,
            clock ?? TimeProvider.System,
            logger ?? NullLogger<NotifyUserCommandKafkaHandler>.Instance);

        var cmd = new NotifyUserCommand
        {
            NotificationId = notificationId,
            RecipientUserId = recipientUserId,
            TemplateKey = templateKey,
            Payload = new Dictionary<string, string> { ["InvoiceNumber"] = "INV-2026-000042" },
            OccurredOnUtc = new DateTime(2026, 6, 9, 0, 0, 0, DateTimeKind.Utc),
        };

        await handler.Handle(TestKafkaMessageContext.Create(ct), cmd);
    }

    private async Task ArrangeInvoiceTemplateAsync(CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Templates.Add(Template.Create("invoicing.invoice-delivered", "Invoice ready."));
        db.TemplateChannels.Add(TemplateChannel.Create(
            "invoicing.invoice-delivered", ChannelType.Email, "Invoice {{InvoiceNumber}}", "Body {{InvoiceNumber}}"));
        await db.SaveChangesAsync(ct);
    }

    private async Task ArrangeOrderShippedTemplateAsync(CancellationToken ct)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Templates.Add(Template.Create("order.shipped", "Order shipped."));
        db.TemplateChannels.AddRange(
            TemplateChannel.Create("order.shipped", ChannelType.Email, "Shipped {{OrderNumber}}", "Body"),
            TemplateChannel.Create("order.shipped", ChannelType.Bell, subject: null, "Bell body"),
            TemplateChannel.Create("order.shipped", ChannelType.Sms, subject: null, "Sms body"));
        await db.SaveChangesAsync(ct);
    }

    private async Task ArrangePreferenceAsync(
        Guid userId,
        IReadOnlyList<ChannelType> enabledChannels,
        CancellationToken ct,
        TimeOnly? quietHoursStart = null,
        TimeOnly? quietHoursEnd = null)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.UserPreferences.Add(NotificationPreference.Create(
            userId,
            email: $"user-{userId:N}@dotnetatlas.test",
            phoneNumber: "+420600000000",
            enabledChannels: enabledChannels,
            quietHoursStart: quietHoursStart,
            quietHoursEnd: quietHoursEnd,
            timeZone: "Europe/Prague"));
        await db.SaveChangesAsync(ct);
    }
}
