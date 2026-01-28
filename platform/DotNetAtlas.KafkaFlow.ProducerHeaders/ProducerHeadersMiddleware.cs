using DotNetAtlas.Messaging.Abstractions;
using KafkaFlow;

namespace DotNetAtlas.KafkaFlow.ProducerHeaders;

/// <summary>
/// Producer middleware that automatically adds MessageId and Origin headers to outgoing messages.
/// This ensures a consistent header population across all KafkaFlow producers.
/// </summary>
/// <remarks>
/// <para>
/// The middleware adds the following headers:
/// <list type="bullet">
///   <item><description><c>message.id</c> - A unique GUID (v7) for idempotent message processing. Only added if not already present.</description></item>
///   <item><description><c>origin</c> - The service identifier that produced the message. Only added if not already present.</description></item>
/// </list>
/// </para>
/// <para>
/// This middleware should be placed at the beginning of the producer middleware pipeline
/// (before serializers) so that headers are available for all downstream middlewares.
/// </para>
/// </remarks>
public class ProducerHeadersMiddleware : IMessageMiddleware
{
    private readonly ProducerHeadersOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProducerHeadersMiddleware"/> class.
    /// </summary>
    /// <param name="options">The configuration options containing the origin identifier.</param>
    public ProducerHeadersMiddleware(
        ProducerHeadersOptions options)
    {
        _options = options;
    }

    /// <inheritdoc/>
    public Task Invoke(IMessageContext context, MiddlewareDelegate next)
    {
        EnsureMessageIdHeader(context);
        EnsureOriginHeader(context);

        return next(context);
    }

    private static void EnsureMessageIdHeader(IMessageContext context)
    {
        var existingMessageId = context.Headers.GetString(MessageHeaderKeys.MessageId);

        if (string.IsNullOrEmpty(existingMessageId))
        {
            var messageId = Guid.CreateVersion7().ToString();
            context.Headers.SetString(MessageHeaderKeys.MessageId, messageId);
        }
    }

    private void EnsureOriginHeader(IMessageContext context)
    {
        var existingOrigin = context.Headers.GetString(MessageHeaderKeys.Origin);

        if (string.IsNullOrEmpty(existingOrigin))
        {
            context.Headers.SetString(MessageHeaderKeys.Origin, _options.Origin);
        }
    }
}
