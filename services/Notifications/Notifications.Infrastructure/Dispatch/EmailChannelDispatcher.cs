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
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Notifications.Infrastructure.Dispatch;

/// <summary>
/// Email channel dispatcher (ADR-0032 § 2). Runs as an isolated Hangfire job. Guards the send with
/// the per-channel <c>(NotificationId, Channel)</c> ledger: if already <c>Dispatched</c>, no-op; else
/// send via MailKit → Mailpit, then <b>UPSERT</b> the ledger row and write the delivery outbox event
/// in <b>one</b> transaction. The first attempt INSERTs (<c>Failed</c> or <c>Dispatched</c>); a later
/// retry of a <c>Failed</c> row UPDATEs it to <c>Dispatched</c> (never a second INSERT). At-least-once.
/// </summary>
internal sealed class EmailChannelDispatcher : IChannelDispatcher
{
    private readonly INotificationsDbContext _db;
    private readonly ITransactionalOutbox<INotificationsDbContext> _outbox;
    private readonly IRecipientResolver _recipientResolver;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly IEmailGateway _gateway;
    private readonly TopicsOptions _topics;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailChannelDispatcher> _logger;

    public EmailChannelDispatcher(
        INotificationsDbContext db,
        ITransactionalOutbox<INotificationsDbContext> outbox,
        IRecipientResolver recipientResolver,
        IEmailTemplateRenderer renderer,
        IEmailGateway gateway,
        IOptions<TopicsOptions> topics,
        TimeProvider clock,
        ILogger<EmailChannelDispatcher> logger)
    {
        _db = db;
        _outbox = outbox;
        _recipientResolver = recipientResolver;
        _renderer = renderer;
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

        var contact = await _recipientResolver.ResolveAsync(dispatch.RecipientUserId, ct);

        var renderResult = _renderer.Render(contact.Email, dispatch.TemplateKey, dispatch.Payload);
        if (renderResult.IsFailed)
        {
            // Bug-class — producer sent an unknown template or malformed payload. Let it surface
            // (Hangfire records the failed job); there is nothing to retry into success.
            throw new InvalidOperationException(
                $"Template render failed for '{dispatch.TemplateKey}': " +
                string.Join("; ", renderResult.Errors.Select(e => e.Message)));
        }

        var sendResult = await _gateway.SendAsync(renderResult.Value, ct);
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
