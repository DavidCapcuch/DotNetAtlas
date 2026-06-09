using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Application.Common.Data;
using Notifications.Application.Common.Messaging;
using Notifications.Application.Dispatch;
using Notifications.Application.Recipients;
using Notifications.Domain.Channels;
using Notifications.Domain.Deliveries;
using Notifications.Domain.Templates;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.SharedKernel.Exceptions;

namespace Notifications.Infrastructure.Dispatch;

/// <summary>
/// Fake SMS channel dispatcher (ADR-0032 § 3, #315). Runs as an isolated Hangfire job — quiet hours
/// are evaluated once, at enqueue time by the Kafka handler; this dispatcher never re-checks them.
/// There is <b>no provider</b>: the "send" is the <c>"Sending SMS…"</c> log line (a real provider is
/// a deferred seam, notifications.md § 13), which is why the fake send cannot fail and the ledger
/// never records <c>Failed</c> on this channel. Everything else follows the durable-channel contract
/// of the email dispatcher (#312). At-least-once.
/// </summary>
internal sealed class SmsChannelDispatcher : IChannelDispatcher
{
    private readonly INotificationsDbContext _db;
    private readonly ITransactionalOutbox<INotificationsDbContext> _outbox;
    private readonly IRecipientResolver _recipientResolver;
    private readonly TopicsOptions _topics;
    private readonly TimeProvider _clock;
    private readonly ILogger<SmsChannelDispatcher> _logger;

    public SmsChannelDispatcher(
        INotificationsDbContext db,
        ITransactionalOutbox<INotificationsDbContext> outbox,
        IRecipientResolver recipientResolver,
        IOptions<TopicsOptions> topics,
        TimeProvider clock,
        ILogger<SmsChannelDispatcher> logger)
    {
        _db = db;
        _outbox = outbox;
        _recipientResolver = recipientResolver;
        _topics = topics.Value;
        _clock = clock;
        _logger = logger;
    }

    public ChannelType Channel => ChannelType.Sms;

    public async Task DispatchAsync(NotificationDispatch dispatch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        var existing = await _db.NotificationDeliveries
            .FirstOrDefaultAsync(
                d => d.NotificationId == dispatch.NotificationId && d.Channel == ChannelType.Sms,
                ct);

        if (existing is { IsDispatched: true })
        {
            _logger.LogInformation(
                "SMS already dispatched for NotificationId={NotificationId}; skipping. TemplateKey={TemplateKey}",
                dispatch.NotificationId,
                dispatch.TemplateKey);
            return;
        }

        // Missing row is bug-class — the producer named a template with no Sms channel. Same
        // fail-fast classification as the email dispatcher: DataIntegrityException parks the job
        // Failed on the first attempt (nothing to retry into success).
        var templateChannel = await _db.TemplateChannels
            .FirstOrDefaultAsync(
                tc => tc.TemplateKey == dispatch.TemplateKey && tc.Channel == ChannelType.Sms,
                ct) ?? throw new DataIntegrityException(
                "Notifications.MissingSmsTemplateChannel",
                $"No Sms template channel found for template key '{dispatch.TemplateKey}'.");

        var contact = await _recipientResolver.ResolveAsync(dispatch.RecipientUserId, ct);

        var body = TemplateRenderer.Render(templateChannel.Body, dispatch.Payload);

        // The renderer leaves unknown tokens literal; recording a half-rendered message as
        // Dispatched would be the same lie as emailing one, so the shared loud-fail guard applies.
        var unresolvedTokens = TemplateRenderer.FindUnresolvedTokens(body);
        if (unresolvedTokens.Count > 0)
        {
            throw new DataIntegrityException(
                "Notifications.UnresolvedTemplateTokens",
                $"Cannot send SMS for template '{dispatch.TemplateKey}': payload is missing values " +
                $"for token(s) {string.Join(", ", unresolvedTokens)}.");
        }

        // The fake send. Logging the phone number is deliberate here — this line IS the channel's
        // transport, and user_preferences.phone_number is fake-by-design E.164 reference data
        // (notifications.md § 8); a real provider integration would move the number into the
        // provider call and out of the logs.
        _logger.LogInformation(
            "Sending SMS to {PhoneNumber} for NotificationId={NotificationId}, TemplateKey={TemplateKey}: {SmsBody}",
            contact.PhoneNumber,
            dispatch.NotificationId,
            dispatch.TemplateKey,
            body);

        var now = _clock.GetUtcNow();

        // Record the outcome and emit the delivery event atomically (ADR-0032 § 2 / § 4). The ledger
        // row and the outbox event are added to the same DbContext and persisted by a single
        // SaveChanges inside one transaction, so they commit together.
        await _db.Database.EnsureTransactionAsync(
            async () =>
            {
                // Re-resolve the ledger row inside the execution-strategy closure (tracker first, then
                // DB) so a transient-fault replay stays idempotent — same rationale as the email
                // dispatcher. A pre-existing row here is the locally-tracked row of a replayed attempt
                // or a concurrently-committed duplicate job's Dispatched row (SMS never writes Failed);
                // the UPSERT re-marks it rather than INSERTing a duplicate on the unique key.
                var row = _db.NotificationDeliveries.Local
                              .FirstOrDefault(d =>
                                  d.NotificationId == dispatch.NotificationId && d.Channel == ChannelType.Sms)
                          ?? await _db.NotificationDeliveries.FirstOrDefaultAsync(
                              d => d.NotificationId == dispatch.NotificationId && d.Channel == ChannelType.Sms,
                              ct);

                if (row is null)
                {
                    _db.NotificationDeliveries.Add(
                        NotificationDelivery.Record(
                            dispatch.NotificationId, ChannelType.Sms, DeliveryStatus.Dispatched, now));
                }
                else
                {
                    row.MarkDispatched(now);
                }

                _outbox.AddOutboxMessage(
                    _topics.NotifyEvents,
                    dispatch.RecipientUserId.ToString(),
                    new NotificationDeliveryStatusChangedEvent
                    {
                        NotificationId = dispatch.NotificationId,
                        RecipientUserId = dispatch.RecipientUserId,
                        TemplateKey = dispatch.TemplateKey,
                        Channel = ChannelType.Sms.Name,
                        Status = NotificationDeliveryStatus.Dispatched,
                        OccurredOnUtc = now.UtcDateTime,
                    });

                await _db.SaveChangesAsync(ct);
            },
            ct);

        _logger.LogInformation(
            "SMS dispatched for NotificationId={NotificationId}, TemplateKey={TemplateKey}, Channel={Channel}",
            dispatch.NotificationId,
            dispatch.TemplateKey,
            ChannelType.Sms.Name);
    }
}
