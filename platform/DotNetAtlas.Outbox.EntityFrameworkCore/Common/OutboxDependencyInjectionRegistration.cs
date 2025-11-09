using Avro.Specific;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Outbox.EntityFrameworkCore.Core;

namespace DotNetAtlas.Outbox.EntityFrameworkCore.Common;

/// <summary>
/// Provides a fluent API for configuring the Outbox pattern.
/// </summary>
public sealed class OutboxDependencyInjectionRegistration
{
    private readonly DomainEventExtractionCache _domainEventExtractor;
    private readonly AvroMappingCache _avroMapper;
    internal AvroSerializerConfig? AvroSerializerOptions { get; private set; }
    internal SchemaRegistryConfig? SchemaRegistryOptions { get; private set; }

    internal OutboxDependencyInjectionRegistration(
        DomainEventExtractionCache domainEventExtractor,
        AvroMappingCache avroMapper)
    {
        _domainEventExtractor = domainEventExtractor;
        _avroMapper = avroMapper;
    }

    /// <summary>
    /// Register how to extract domain events and kafka key from an entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="extractor">Function that extracts aggregate data from the entity.</param>
    /// <returns>This registration for chaining.</returns>
    public OutboxDependencyInjectionRegistration RegisterOutboxMessagesBatchExtractionFor<TEntity>(
        Func<TEntity, OutboxMessagesBatch> extractor)
    {
        _domainEventExtractor.RegisterEventExtractor(extractor);

        return this;
    }

    /// <summary>
    /// Register how to map a domain event to its Avro representation.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type.</typeparam>
    /// <param name="mapper">Function that converts the event to Avro.</param>
    /// <returns>This registration for chaining.</returns>
    public OutboxDependencyInjectionRegistration RegisterAvroMapperFor<TEvent>(
        Func<TEvent, ISpecificRecord> mapper)
    {
        _avroMapper.RegisterAvroMapper(mapper);

        return this;
    }

    /// <summary>
    /// Configure the Avro serializer settings using a configuration action.
    /// </summary>
    /// <param name="configure">Configuration action for AvroSerializerConfig.</param>
    /// <returns>This registration for chaining.</returns>
    public OutboxDependencyInjectionRegistration ConfigureAvroSerializerConfig(Action<AvroSerializerConfig> configure)
    {
        AvroSerializerOptions = new AvroSerializerConfig();
        configure(AvroSerializerOptions);

        return this;
    }

    /// <summary>
    /// Configure the Avro serializer settings using a configuration action.
    /// </summary>
    /// <param name="configure">Configuration action for AvroSerializerConfig.</param>
    /// <returns>This registration for chaining.</returns>
    public OutboxDependencyInjectionRegistration ConfigureSchemaRegistryConfig(Action<SchemaRegistryConfig> configure)
    {
        SchemaRegistryOptions = new SchemaRegistryConfig();
        configure(SchemaRegistryOptions);

        return this;
    }
}
