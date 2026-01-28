using System.Text;
using DotNetAtlas.Messaging.Abstractions;
using KafkaFlow;

namespace DotNetAtlas.KafkaFlow.Inbox.EFCore;

/// <summary>
/// Extension methods for extracting values from Kafka message headers.
/// </summary>
public static class KafkaMessageHeaderExtensions
{
    /// <param name="context">The Kafka message context.</param>
    extension(IMessageContext context)
    {
        /// <summary>
        /// Extracts the MessageId from Kafka message headers.
        /// </summary>
        /// <returns>The MessageId if found and valid, otherwise null.</returns>
        public Guid? ExtractMessageId()
        {
            var messageIdHeaderValue = context.ExtractHeader(MessageHeaderKeys.MessageId);

            if (messageIdHeaderValue is null)
            {
                return null;
            }

            return Guid.TryParse(messageIdHeaderValue, out var messageId) ? messageId : null;
        }

        /// <summary>
        /// Extracts the Origin from Kafka message headers.
        /// </summary>
        /// <returns>The origin service name if found, otherwise null.</returns>
        public string? ExtractOrigin()
        {
            return context.ExtractHeader(MessageHeaderKeys.Origin);
        }

        /// <summary>
        /// Extracts a string value from Kafka message headers.
        /// </summary>
        /// <param name="headerKey">The header key to extract.</param>
        /// <returns>The header value if found, otherwise null.</returns>
        public string? ExtractHeader(string headerKey)
        {
            var header = context.Headers.FirstOrDefault(h => h.Key == headerKey);

            return header.Value is null ? null : Encoding.UTF8.GetString(header.Value);
        }
    }
}
