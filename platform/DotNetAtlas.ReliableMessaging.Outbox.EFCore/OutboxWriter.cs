using System.Diagnostics;
using Avro.Specific;
using DotNetAtlas.Messaging.Abstractions;
using DotNetAtlas.ReliableMessaging.Outbox.Core;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.ReliableMessaging.Outbox.EFCore;

/// <summary>
/// Core implementation of <see cref="IOutboxWriter"/> that handles Avro serialization
/// and outbox message creation.
/// </summary>
/// <remarks>
/// This class is registered as a Singleton since it only depends on stateless services.
/// All mutable states (the outbox messages) are stored in the DbContext passed to each method.
/// </remarks>
public class OutboxWriter : IOutboxWriter
{
    private readonly UniversalAvroSerializer _universalAvroSerializer;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<OutboxOptions> _outboxOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxWriter"/> class.
    /// </summary>
    /// <param name="universalAvroSerializer">The Avro serializer for message serialization.</param>
    /// <param name="timeProvider">The time provider for timestamps.</param>
    /// <param name="outboxOptions">The outbox configuration options.</param>
    public OutboxWriter(
        UniversalAvroSerializer universalAvroSerializer,
        TimeProvider timeProvider,
        IOptions<OutboxOptions> outboxOptions)
    {
        _universalAvroSerializer = universalAvroSerializer;
        _timeProvider = timeProvider;
        _outboxOptions = outboxOptions;
    }

    /// <inheritdoc />
    public void AddOutboxMessage(IOutboxDbContext dbContext, string topicName, string? kafkaKey, ISpecificRecord integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var messageOrigin = _outboxOptions.Value.MessageOrigin;

        var messageType = integrationEvent.GetType();
        var bytes = _universalAvroSerializer.Serialize(integrationEvent, messageType);

        var activity = Activity.Current;
        var headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(activity) ?? [];

        if (!string.IsNullOrEmpty(messageOrigin))
        {
            headers[MessageHeaderKeys.Origin] = messageOrigin;
        }

        headers[MessageHeaderKeys.MessageId] = Guid.CreateVersion7().ToString();

        var outboxMessage = new OutboxMessage
        {
            TopicName = topicName,
            KafkaKey = kafkaKey,
            AvroPayload = bytes,
            Type = messageType.FullName ?? messageType.Name,
            CreatedUtc = _timeProvider.GetUtcNow(),
            Headers = OutboxMessageHeaderExtensions.SerializeHeaders(headers)
        };

        dbContext.OutboxMessages.Add(outboxMessage);
    }
}
