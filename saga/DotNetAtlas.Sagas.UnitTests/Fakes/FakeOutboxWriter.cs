using System.Collections.Concurrent;
using Avro.Specific;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;

namespace DotNetAtlas.Sagas.UnitTests.Fakes;

/// <summary>
/// Fake implementation of <see cref="IOutboxWriter"/> that captures messages for verification in tests.
/// </summary>
/// <remarks>
/// This allows saga tests to verify that the correct messages were added to the outbox
/// without requiring actual Kafka/database infrastructure.
/// Thread-safe for concurrent test scenarios.
/// </remarks>
public sealed class FakeOutboxWriter : IOutboxWriter
{
    private readonly ConcurrentBag<OutboxMessage> _messages = [];

    /// <summary>
    /// Gets all messages that have been added to the outbox.
    /// </summary>
    public IReadOnlyCollection<OutboxMessage> CapturedMessages => _messages.ToArray();

    /// <inheritdoc />
    public void AddOutboxMessage(IOutboxDbContext dbContext,
        string topicName,
        string? kafkaKey,
        ISpecificRecord integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(integrationEvent);

        _messages.Add(new OutboxMessage(topicName, kafkaKey, integrationEvent));
    }

    /// <summary>
    /// Clears all captured messages. Useful for test setup/teardown.
    /// </summary>
    public void Clear() => _messages.Clear();

    /// <summary>
    /// Gets all messages of a specific type.
    /// </summary>
    /// <typeparam name="TMessage">The Avro message type to filter by.</typeparam>
    /// <returns>Messages matching the specified type.</returns>
    public IEnumerable<OutboxMessage<TMessage>> GetMessages<TMessage>()
        where TMessage : ISpecificRecord
    {
        return _messages
            .Where(m => m.IntegrationEvent is TMessage)
            .Select(m => new OutboxMessage<TMessage>(m.TopicName, m.KafkaKey, (TMessage)m.IntegrationEvent));
    }

    /// <summary>
    /// Checks if any message of the specified type was added to the outbox.
    /// </summary>
    /// <typeparam name="TMessage">The Avro message type to check for.</typeparam>
    /// <returns>True if at least one message of the specified type exists.</returns>
    public bool HasMessage<TMessage>()
        where TMessage : ISpecificRecord
    {
        return _messages.Any(m => m.IntegrationEvent is TMessage);
    }

    /// <summary>
    /// Represents a captured outbox message.
    /// </summary>
    /// <param name="TopicName">The Kafka topic where the message would be published.</param>
    /// <param name="KafkaKey">The Kafka key for partitioning.</param>
    /// <param name="IntegrationEvent">The Avro integration event.</param>
    public sealed record OutboxMessage(string TopicName, string? KafkaKey, ISpecificRecord IntegrationEvent);

    /// <summary>
    /// Represents a captured outbox message with a strongly-typed event.
    /// </summary>
    /// <typeparam name="TMessage">The Avro message type.</typeparam>
    /// <param name="TopicName">The Kafka topic where the message would be published.</param>
    /// <param name="KafkaKey">The Kafka key for partitioning.</param>
    /// <param name="IntegrationEvent">The Avro integration event.</param>
    public sealed record OutboxMessage<TMessage>(string TopicName, string? KafkaKey, TMessage IntegrationEvent)
        where TMessage : ISpecificRecord;
}
