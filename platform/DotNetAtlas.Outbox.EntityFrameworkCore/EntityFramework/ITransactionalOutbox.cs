using Avro.Specific;

namespace DotNetAtlas.Outbox.EntityFrameworkCore.EntityFramework;

/// <summary>
/// Service for publishing integration events to the outbox.
/// Use in domain event handlers to map domain events to integration events
/// and persist them in the same transaction as the domain state change.
/// </summary>
/// <remarks>
/// This service is scoped and uses the same DbContext instance as the domain event handler,
/// ensuring that integration events are persisted in the same transaction as the aggregate changes.
/// Similar to MassTransit's IPublishEndpoint with transactional outbox enabled.
/// </remarks>
public interface ITransactionalOutbox
{
    /// <summary>
    /// Publishes an integration event to the outbox table.
    /// The event will be persisted in the same transaction as other changes to the scoped DbContext.
    /// </summary>
    /// <param name="kafkaKey">The Kafka key for message partitioning (typically aggregate ID).</param>
    /// <param name="integrationEvent">The Avro integration event to publish.</param>
    void Publish(string kafkaKey, ISpecificRecord integrationEvent);
}
