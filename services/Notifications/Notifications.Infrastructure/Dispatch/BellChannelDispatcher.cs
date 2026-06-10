using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Application.Bell;
using Notifications.Application.Common.Data;
using Notifications.Application.Dispatch;
using Notifications.Domain.Channels;
using Notifications.Domain.Templates;
using Platform.SharedKernel.Exceptions;

namespace Notifications.Infrastructure.Dispatch;

/// <summary>
/// Bell channel dispatcher (ADR-0032 § 3, #317). Renders the <c>Bell</c> <c>template_channels</c>
/// body and pushes it through <see cref="INotificationBroadcaster"/> to the recipient's SignalR
/// group — the group key IS <c>RecipientUserId</c>, so unlike email/SMS there is no recipient
/// resolution. The bell is <b>ephemeral</b>: no <c>notification_deliveries</c> ledger row, no
/// delivery event, no transaction, no duplicate-check — an offline recipient misses the bell
/// entirely (accepted best-effort behaviour; durability is the deferred seam of
/// notifications.md § 13).
/// </summary>
internal sealed class BellChannelDispatcher : IChannelDispatcher
{
    private readonly INotificationsDbContext _db;
    private readonly INotificationBroadcaster _broadcaster;
    private readonly ILogger<BellChannelDispatcher> _logger;

    public BellChannelDispatcher(
        INotificationsDbContext db,
        INotificationBroadcaster broadcaster,
        ILogger<BellChannelDispatcher> logger)
    {
        _db = db;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public ChannelType Channel => ChannelType.Bell;

    public async Task DispatchAsync(NotificationDispatch dispatch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        // Missing row is bug-class — the producer named a template with no Bell channel. Same
        // fail-fast classification as the email/SMS dispatchers: DataIntegrityException parks the
        // job Failed on the first attempt (nothing to retry into success).
        var templateChannel = await _db.TemplateChannels
            .FirstOrDefaultAsync(
                tc => tc.TemplateKey == dispatch.TemplateKey && tc.Channel == ChannelType.Bell,
                ct) ?? throw new DataIntegrityException(
                "Notifications.MissingBellTemplateChannel",
                $"No Bell template channel found for template key '{dispatch.TemplateKey}'.");

        var body = TemplateRenderer.Render(templateChannel.Body, dispatch.Payload);

        // The renderer leaves unknown tokens literal; pushing a half-rendered bell would be the
        // same lie as emailing one, so the shared loud-fail guard applies.
        var unresolvedTokens = TemplateRenderer.FindUnresolvedTokens(body);
        if (unresolvedTokens.Count > 0)
        {
            throw new DataIntegrityException(
                "Notifications.UnresolvedTemplateTokens",
                $"Cannot push bell for template '{dispatch.TemplateKey}': payload is missing values " +
                $"for token(s) {string.Join(", ", unresolvedTokens)}.");
        }

        // A group-send to zero live connections is a successful no-op — offline = missed, by design.
        await _broadcaster.PushToUserAsync(dispatch.RecipientUserId, new BellNotification(body), ct);

        _logger.LogInformation(
            "Bell pushed for NotificationId={NotificationId}, TemplateKey={TemplateKey}, Channel={Channel}",
            dispatch.NotificationId,
            dispatch.TemplateKey,
            ChannelType.Bell.Name);
    }
}
