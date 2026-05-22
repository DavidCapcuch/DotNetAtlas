using KafkaFlow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Common.Config;
using Notifications.Common.Persistence.Database;
using Notifications.Email;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Notifications.Notifications.SendEmailNotification;

/// <summary>
/// Consumes generic SendEmailNotificationCommand from notifications.email-commands.
/// Renders via IEmailTemplateRenderer, sends via IEmailGateway, and on success emits
/// EmailNotificationSentEvent to notifications.email-events. Inbox-deduped via
/// IdempotencyKey copied to the inbox primary key by KafkaFlow's AddInbox middleware.
/// </summary>
public sealed class SendEmailNotificationCommandKafkaHandler : IMessageHandler<SendEmailNotificationCommand>
{
    private readonly ITransactionalOutbox<INotificationDbContext> _outbox;
    private readonly IEmailGateway _gateway;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly TopicsOptions _topics;
    private readonly TimeProvider _clock;
    private readonly ILogger<SendEmailNotificationCommandKafkaHandler> _logger;

    public SendEmailNotificationCommandKafkaHandler(
        ITransactionalOutbox<INotificationDbContext> outbox,
        IEmailGateway gateway,
        IEmailTemplateRenderer renderer,
        IOptions<TopicsOptions> topics,
        TimeProvider clock,
        ILogger<SendEmailNotificationCommandKafkaHandler> logger)
    {
        _outbox = outbox;
        _gateway = gateway;
        _renderer = renderer;
        _topics = topics.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, SendEmailNotificationCommand cmd)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cmd);

        var token = context.ConsumerContext.WorkerStopped;

        await _outbox.Database.EnsureTransactionAsync(async () =>
        {
            var renderResult = _renderer.Render(cmd.UserId.ToString(), cmd.TemplateId, cmd.TemplateData);
            if (renderResult.IsFailed)
            {
                // Bug-class — producer sent an unknown template or malformed data.
                _logger.LogError(
                    "EmailTemplateRenderer.Render failed for TemplateId={TemplateId}; IdempotencyKey={Key}; errors={Errors}",
                    cmd.TemplateId, cmd.IdempotencyKey,
                    string.Join("; ", renderResult.Errors.Select(e => e.Message)));
                throw new InvalidOperationException(
                    $"Template render failed: {string.Join("; ", renderResult.Errors.Select(e => e.Message))}");
            }

            var sendResult = await _gateway.SendAsync(renderResult.Value, token);
            if (sendResult.IsFailed)
            {
                // Transient — let KafkaFlow retry; eventually DLT.
                throw new InvalidOperationException(
                    $"Email gateway failed: {string.Join("; ", sendResult.Errors.Select(e => e.Message))}");
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            _outbox.AddOutboxMessage(_topics.EmailEvents, cmd.UserId.ToString(), new EmailNotificationSentEvent
            {
                UserId = cmd.UserId,
                TemplateId = cmd.TemplateId,
                IdempotencyKey = cmd.IdempotencyKey,
                SentAtUtc = now,
                OccurredOnUtc = now,
            });
            await _outbox.SaveChangesAsync(token);

            _logger.LogInformation(
                "Email sent and EmailNotificationSentEvent queued. UserId={UserId}, TemplateId={TemplateId}, IdempotencyKey={Key}",
                cmd.UserId, cmd.TemplateId, cmd.IdempotencyKey);
        }, token);
    }
}
