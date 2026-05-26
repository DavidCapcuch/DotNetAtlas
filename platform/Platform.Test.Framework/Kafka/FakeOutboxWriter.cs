using System.Collections.Concurrent;
using Avro.Specific;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Platform.Test.Framework.Kafka;

/// <summary>
/// Test-side <see cref="IOutboxWriter"/> that does two things in one call:
/// <list type="number">
///   <item>
///     Inserts a stub <see cref="OutboxMessage"/> row into the supplied
///     <see cref="IOutboxDbContext"/> with an empty Avro payload so integration
///     tests can exercise the real outbox-relay path against Postgres without
///     standing up Schema Registry. Topic name, Kafka key, and the integration
///     event's CLR type are preserved.
///   </item>
///   <item>
///     Captures the same call in-memory for assertion helpers
///     (<see cref="CapturedMessages"/>, <see cref="GetMessages{TMessage}"/>,
///     <see cref="HasMessage{TMessage}"/>) used by saga unit tests that bypass
///     the database entirely.
///   </item>
/// </list>
/// Thread-safe for concurrent test scenarios.
/// </summary>
public sealed class FakeOutboxWriter : IOutboxWriter
{
    private readonly ConcurrentBag<CapturedOutboxMessage> _messages = [];

    /// <summary>
    /// All messages captured in-memory across the lifetime of this writer.
    /// </summary>
    public IReadOnlyCollection<CapturedOutboxMessage> CapturedMessages => _messages.ToArray();

    /// <inheritdoc />
    public void AddOutboxMessage(
        IOutboxDbContext dbContext,
        string topicName,
        string? kafkaKey,
        ISpecificRecord integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var messageType = integrationEvent.GetType();

        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            TopicName = topicName,
            KafkaKey = kafkaKey,
            AvroPayload = [],
            Type = messageType.FullName ?? messageType.Name,
            CreatedUtc = DateTimeOffset.UtcNow,
        });

        _messages.Add(new CapturedOutboxMessage(topicName, kafkaKey, integrationEvent));
    }

    /// <summary>
    /// Clears all captured in-memory messages. Useful for test setup/teardown.
    /// Does not affect any rows already inserted into the DbContext.
    /// </summary>
    public void Clear() => _messages.Clear();

    /// <summary>
    /// Gets all captured messages of a specific Avro type.
    /// </summary>
    /// <typeparam name="TMessage">The Avro message type to filter by.</typeparam>
    public IEnumerable<CapturedOutboxMessage<TMessage>> GetMessages<TMessage>()
        where TMessage : ISpecificRecord
        => _messages
            .Where(m => m.IntegrationEvent is TMessage)
            .Select(m => new CapturedOutboxMessage<TMessage>(m.TopicName, m.KafkaKey, (TMessage)m.IntegrationEvent));

    /// <summary>
    /// Returns whether any captured message of the specified Avro type exists.
    /// </summary>
    /// <typeparam name="TMessage">The Avro message type to check for.</typeparam>
    public bool HasMessage<TMessage>()
        where TMessage : ISpecificRecord
        => _messages.Any(m => m.IntegrationEvent is TMessage);

    /// <summary>A captured outbox call (in-memory view).</summary>
    /// <param name="TopicName">The Kafka topic the message would be published to.</param>
    /// <param name="KafkaKey">The Kafka key (for partitioning).</param>
    /// <param name="IntegrationEvent">The Avro integration event.</param>
    public sealed record CapturedOutboxMessage(string TopicName, string? KafkaKey, ISpecificRecord IntegrationEvent);

    /// <summary>A captured outbox call with a strongly-typed Avro event.</summary>
    /// <typeparam name="TMessage">The Avro message type.</typeparam>
    /// <param name="TopicName">The Kafka topic the message would be published to.</param>
    /// <param name="KafkaKey">The Kafka key (for partitioning).</param>
    /// <param name="IntegrationEvent">The Avro integration event.</param>
    public sealed record CapturedOutboxMessage<TMessage>(string TopicName, string? KafkaKey, TMessage IntegrationEvent)
        where TMessage : ISpecificRecord;
}
