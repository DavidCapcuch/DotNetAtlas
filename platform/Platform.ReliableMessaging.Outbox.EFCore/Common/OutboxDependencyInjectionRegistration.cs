using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace Platform.ReliableMessaging.Outbox.EFCore.Common;

/// <summary>
/// Provides a fluent API for configuring the Outbox pattern.
/// </summary>
public sealed class OutboxDependencyInjectionRegistration
{
    internal AvroSerializerConfig? AvroSerializerOptions { get; private set; }
    internal SchemaRegistryConfig? SchemaRegistryOptions { get; private set; }

    /// <summary>
    /// Origin identifier for the service producing outbox messages.
    /// Added to message headers for tracking event sources across services.
    /// If null, the origin header is not added.
    /// </summary>
    internal string? MessageOrigin { get; private set; }

    /// <summary>
    /// Configure the origin identifier for outbox messages.
    /// The origin is added to message headers to track the service that produced the event.
    /// </summary>
    /// <param name="messageOrigin">The origin identifier (e.g., "MyService_Api").</param>
    /// <returns>This registration for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when messageOrigin is null or whitespace.</exception>
    public OutboxDependencyInjectionRegistration ConfigureMessageOrigin(string messageOrigin)
    {
        if (string.IsNullOrWhiteSpace(messageOrigin))
        {
            throw new ArgumentException("Message origin cannot be null or whitespace.", nameof(messageOrigin));
        }

        MessageOrigin = messageOrigin;

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
    /// Configure the Schema Registry settings using a configuration action.
    /// </summary>
    /// <param name="configure">Configuration action for SchemaRegistryConfig.</param>
    /// <returns>This registration for chaining.</returns>
    public OutboxDependencyInjectionRegistration ConfigureSchemaRegistryConfig(Action<SchemaRegistryConfig> configure)
    {
        SchemaRegistryOptions = new SchemaRegistryConfig();
        configure(SchemaRegistryOptions);

        return this;
    }
}
