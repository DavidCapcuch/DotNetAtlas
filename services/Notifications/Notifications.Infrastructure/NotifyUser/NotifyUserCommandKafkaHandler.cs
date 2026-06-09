using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.Common.Data;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Notifications.Domain.Preferences;
using Platform.SharedKernel.Exceptions;

namespace Notifications.Infrastructure.NotifyUser;

/// <summary>
/// Consumes <c>NotifyUserCommand</c> from <c>notifications.notify-commands</c>. Runs inside the platform
/// <c>InboxMiddleware</c> transaction (dedup on the <c>message.id</c> header). Resolves the dispatch
/// channels — <c>enabled_channels ∩ template_channels</c> (<see cref="ChannelResolver"/>, notifications.md
/// § 5.3) — computes a per-channel <c>ExecuteAt</c> (<see cref="QuietHoursCalculator"/> for
/// quiet-hours-respecting channels, § 5.4) and enqueues one isolated background job per resolved channel.
/// Any enqueue failure throws → the inbox row rolls back → Kafka re-drives the whole fan-out (duplicate
/// jobs absorbed by the per-channel ledger). ADR-0031/0032.
/// </summary>
/// <remarks>
/// Two empty-resolution cases are deliberately treated differently:
/// <list type="bullet">
/// <item>A template with <b>no</b> channel rows is an unknown / misconfigured template — a producer bug —
/// so this loud-fails with <see cref="DataIntegrityException"/> (→ DLT), never a silent drop.</item>
/// <item>A known template whose supported channels the recipient has all disabled (or a recipient with no
/// preference row at all) resolves to <b>nothing</b> — a valid outcome (no mandatory-channel floor,
/// § 5.3) — so it no-ops with a warning, not an exception.</item>
/// </list>
/// </remarks>
public sealed class NotifyUserCommandKafkaHandler : IMessageHandler<NotifyUserCommand>
{
    private readonly INotificationsDbContext _db;
    private readonly IChannelDispatchEnqueuer _enqueuer;
    private readonly TimeProvider _clock;
    private readonly ILogger<NotifyUserCommandKafkaHandler> _logger;

    public NotifyUserCommandKafkaHandler(
        INotificationsDbContext db,
        IChannelDispatchEnqueuer enqueuer,
        TimeProvider clock,
        ILogger<NotifyUserCommandKafkaHandler> logger)
    {
        _db = db;
        _enqueuer = enqueuer;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, NotifyUserCommand cmd)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cmd);

        var ct = context.ConsumerContext.WorkerStopped;

        var supportedChannels = await _db.TemplateChannels
            .Where(tc => tc.TemplateKey == cmd.TemplateKey)
            .Select(tc => tc.Channel)
            .ToListAsync(ct);

        if (supportedChannels.Count == 0)
        {
            // Unknown / unconfigured template — the producer named a template that supports no channels.
            // Bug-class (→ DLT); never silently drop a notification a producer asked us to send.
            throw new DataIntegrityException(
                "Notifications.UnknownTemplate",
                $"NotifyUserCommand for NotificationId={cmd.NotificationId} named template '{cmd.TemplateKey}', " +
                "which has no template channels.");
        }

        var preference = await _db.UserPreferences
            .Where(p => p.UserId == cmd.RecipientUserId)
            .Select(p => new { p.EnabledChannels, p.QuietHoursStart, p.QuietHoursEnd, p.TimeZone })
            .FirstOrDefaultAsync(ct);

        var resolvedChannels = ChannelResolver.Resolve(preference?.EnabledChannels ?? [], supportedChannels);

        if (resolvedChannels.Count == 0)
        {
            _logger.LogWarning(
                "No channels resolved for NotificationId={NotificationId}, TemplateKey={TemplateKey} " +
                "(recipient has none of the template's channels enabled, or no preference row); nothing enqueued.",
                cmd.NotificationId,
                cmd.TemplateKey);
            return;
        }

        var dispatch = new NotificationDispatch
        {
            NotificationId = cmd.NotificationId,
            RecipientUserId = cmd.RecipientUserId,
            TemplateKey = cmd.TemplateKey,
            Payload = new Dictionary<string, string>(cmd.Payload),
        };

        var now = _clock.GetUtcNow();

        // One dispatch instance is shared across channels intentionally: the enqueuer serialises a
        // snapshot per Hangfire job and NotificationDispatch is treated as read-only downstream, so
        // there is no cross-channel aliasing. A non-empty resolution implies a preference row existed
        // (no row → empty enabled set → early return above), hence the null-forgiveness.
        foreach (var channel in resolvedChannels)
        {
            var executeAt = channel.RespectsQuietHours
                ? QuietHoursCalculator.NextAllowedUtc(
                    now, preference!.QuietHoursStart, preference.QuietHoursEnd, preference.TimeZone)
                : now;

            if (executeAt > now)
            {
                _logger.LogInformation(
                    "Quiet hours, deferred to {ExecuteAt}: {Channel} dispatch for NotificationId={NotificationId}, TemplateKey={TemplateKey}",
                    executeAt,
                    channel.Name,
                    cmd.NotificationId,
                    cmd.TemplateKey);
            }

            _enqueuer.Enqueue(channel, dispatch, executeAt);
        }

        _logger.LogInformation(
            "Enqueued {ChannelCount} channel dispatch job(s) [{Channels}] for NotificationId={NotificationId}, TemplateKey={TemplateKey}",
            resolvedChannels.Count,
            string.Join(", ", resolvedChannels.Select(channel => channel.Name)),
            cmd.NotificationId,
            cmd.TemplateKey);
    }
}
