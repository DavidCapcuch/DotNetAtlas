using Avro.Specific;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Inventory.IntegrationTests.Common;

/// <summary>
/// Test-side <see cref="IOutboxWriter"/> that bypasses Avro serialization +
/// Schema Registry calls. Inserts a stub <see cref="OutboxMessage"/> row with
/// the topic name, key, and CLR type name preserved — enough for tests to
/// assert "the right message landed in the right topic" without standing up
/// a Schema Registry container. End-to-end Avro byte-level fidelity is
/// validated in M7 (per the Ordering precedent).
/// </summary>
internal sealed class FakeOutboxWriter : IOutboxWriter
{
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
    }
}
