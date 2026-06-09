using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Application.Common.Data;
using Notifications.Application.Common.Messaging;
using Notifications.Application.Dispatch;
using Notifications.Application.Email;
using Notifications.Application.Recipients;
using Notifications.Domain.Channels;
using Notifications.Domain.Deliveries;
using Notifications.Domain.Templates;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Notifications.Infrastructure.Dispatch;

/// <summary>
/// Email channel dispatcher (ADR-0032 § 2). Runs as an isolated Hangfire job. Loads the
/// <c>(TemplateKey, Email)</c> row from <c>template_channels</c> and renders its subject/body against
/// the command payload via the pure <see cref="TemplateRenderer"/> (#313 — replaces the inline stub);
/// loud-fails (no send) if the payload leaves any <c>{{token}}</c> unresolved, rather than email a
/// half-rendered message. Guards the send with the per-channel <c>(NotificationId, Channel)</c> ledger: if already
/// <c>Dispatched</c>, no-op; else send via MailKit → Mailpit, then <b>UPSERT</b> the ledger row and
/// write the delivery outbox event in <b>one</b> transaction. The first attempt INSERTs
/// (<c>Failed</c> or <c>Dispatched</c>); a later retry of a <c>Failed</c> row UPDATEs it to
/// <c>Dispatched</c> (never a second INSERT). At-least-once.
/// </summary>
internal sealed class EmailChannelDispatcher : IChannelDispatcher
{
    private readonly INotificationsDbContext _db;
    private readonly ITransactionalOutbox<INotificationsDbContext> _outbox;
    private readonly IRecipientResolver _recipientResolver;
    private readonly IEmailGateway _gateway;
    private readonly TopicsOptions _topics;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailChannelDispatcher> _logger;

    public EmailChannelDispatcher(
        INotificationsDbContext db,
        ITransactionalOutbox<INotificationsDbContext> outbox,
        IRecipientResolver recipientResolver,
        IEmailGateway gateway,
        IOptions<TopicsOptions> topics,
        TimeProvider clock,
        ILogger<EmailChannelDispatcher> logger)
    {
        _db = db;
        _outbox = outbox;
        _recipientResolver = recipientResolver;
        _gateway = gateway;
        _topics = topics.Value;
        _clock = clock;
        _logger = logger;
    }

    public ChannelType Channel => ChannelType.Email;

    public async Task DispatchAsync(NotificationDispatch dispatch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        var existing = await _db.NotificationDeliveries
            .FirstOrDefaultAsync(
                d => d.NotificationId == dispatch.NotificationId && d.Channel == ChannelType.Email,
                ct);

        if (existing is { IsDispatched: true })
        {
            _logger.LogInformation(
                "Email already dispatched for NotificationId={NotificationId}; skipping. TemplateKey={TemplateKey}",
                dispatch.NotificationId,
                dispatch.TemplateKey);
            return;
        }

        // Missing row is bug-class — the producer named a template with no Email channel (unknown key,
        // or a template that does not support email). Let it surface (Hangfire records the failed job);
        // there is nothing to retry into success.
        var templateChannel = await _db.TemplateChannels
            .FirstOrDefaultAsync(
                tc => tc.TemplateKey == dispatch.TemplateKey && tc.Channel == ChannelType.Email,
                ct) ?? throw new InvalidOperationException(
                $"No Email template channel found for template key '{dispatch.TemplateKey}'.");

        if (string.IsNullOrWhiteSpace(templateChannel.Subject))
        {
            // Email requires a subject; a null/blank subject is a misconfigured template (bug-class).
            throw new InvalidOperationException(
                $"Email template '{dispatch.TemplateKey}' has no subject.");
        }

        var contact = await _recipientResolver.ResolveAsync(dispatch.RecipientUserId, ct);

        // Dumb {{token}} render over the payload — unknown tokens stay literal (TemplateRenderer is a
        // pure substitution, no failure mode). The renderer leaving literals is not the same as it
        // being OK to SEND them: the dispatcher must not email a customer a half-rendered message and
        // then falsely record Dispatched. So it loud-fails (bug-class, no send → Hangfire failed job)
        // on any token the payload didn't resolve — restoring #312's reject-incomplete-payload guard.
        var subject = TemplateRenderer.Render(templateChannel.Subject, dispatch.Payload);
        var body = TemplateRenderer.Render(templateChannel.Body, dispatch.Payload);

        var unresolvedTokens = TemplateRenderer.FindUnresolvedTokens(subject)
            .Concat(TemplateRenderer.FindUnresolvedTokens(body))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unresolvedTokens.Length > 0)
        {
            throw new InvalidOperationException(
                $"Cannot send email for template '{dispatch.TemplateKey}': payload is missing values " +
                $"for token(s) {string.Join(", ", unresolvedTokens)}.");
        }

        var message = new EmailMessage(contact.Email, subject, body);

        var sendResult = await _gateway.SendAsync(message, ct);
        var status = sendResult.IsSuccess ? DeliveryStatus.Dispatched : DeliveryStatus.Failed;
        var now = _clock.GetUtcNow();

        // Record the outcome and emit the delivery event atomically (ADR-0032 § 2 / § 4). The ledger
        // row and the outbox event are added to the same DbContext and persisted by a single
        // SaveChanges inside one transaction, so they commit together.
        await _db.Database.EnsureTransactionAsync(
            async () =>
            {
                // Re-resolve the ledger row inside the execution-strategy closure (tracker first, then
                // DB). `EnsureTransactionAsync` runs this block under the DbContext's retrying execution
                // strategy, so a transient fault re-invokes it; resolving the already-tracked row here —
                // rather than reusing the row loaded outside — keeps the block idempotent (no duplicate
                // INSERT of the client-keyed (NotificationId, Channel) row on replay).
                var row = _db.NotificationDeliveries.Local
                              .FirstOrDefault(d =>
                                  d.NotificationId == dispatch.NotificationId && d.Channel == ChannelType.Email)
                          ?? await _db.NotificationDeliveries.FirstOrDefaultAsync(
                              d => d.NotificationId == dispatch.NotificationId && d.Channel == ChannelType.Email,
                              ct);

                if (row is null)
                {
                    _db.NotificationDeliveries.Add(
                        NotificationDelivery.Record(dispatch.NotificationId, ChannelType.Email, status, now));
                }
                else if (status == DeliveryStatus.Dispatched)
                {
                    row.MarkDispatched(now);
                }
                else
                {
                    row.MarkFailed(now);
                }

                _outbox.AddOutboxMessage(
                    _topics.NotifyEvents,
                    dispatch.RecipientUserId.ToString(),
                    new NotificationDeliveryStatusChangedEvent
                    {
                        NotificationId = dispatch.NotificationId,
                        RecipientUserId = dispatch.RecipientUserId,
                        TemplateKey = dispatch.TemplateKey,
                        Channel = ChannelType.Email.Name,
                        Status = status == DeliveryStatus.Dispatched
                            ? NotificationDeliveryStatus.Dispatched
                            : NotificationDeliveryStatus.Failed,
                        OccurredOnUtc = now.UtcDateTime,
                    });

                await _db.SaveChangesAsync(ct);
            },
            ct);

        if (sendResult.IsFailed)
        {
            // Failure recorded (ledger + Failed event committed above); rethrow so Hangfire retries.
            // A later successful retry flips the same ledger row to Dispatched.
            throw new InvalidOperationException(
                $"Email send failed for NotificationId={dispatch.NotificationId}: " +
                string.Join("; ", sendResult.Errors.Select(e => e.Message)));
        }

        _logger.LogInformation(
            "Email dispatched for NotificationId={NotificationId}, TemplateKey={TemplateKey}, Channel={Channel}",
            dispatch.NotificationId,
            dispatch.TemplateKey,
            ChannelType.Email.Name);
    }
}
