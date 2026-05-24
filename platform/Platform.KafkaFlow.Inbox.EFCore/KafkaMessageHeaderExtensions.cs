using System.Text;
using KafkaFlow;
using Platform.Messaging.Abstractions;

namespace Platform.KafkaFlow.Inbox.EFCore;

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

        /// <summary>
        /// Extracts the authoritative <c>CorrelationId</c> from the Kafka header
        /// (<see cref="MessageHeaderKeys.CorrelationId"/>) per ADR-0008. The header is the contract;
        /// the Avro payload <c>CorrelationId</c> field is convenience metadata only and must NOT be
        /// read for propagation — consumer business logic that needs the correlation key should call
        /// this helper so the read side and the producer side cannot silently diverge.
        /// </summary>
        /// <returns>The <see cref="Guid"/> if the header is present and parses as a GUID; otherwise
        /// <c>null</c>. UUID v7 validation is the consumer middleware's job — this helper only parses.</returns>
        public Guid? ExtractCorrelationId()
        {
            var raw = context.ExtractHeader(MessageHeaderKeys.CorrelationId);
            if (raw is null)
            {
                return null;
            }

            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }
}
