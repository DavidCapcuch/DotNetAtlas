using System.Collections.Concurrent;
using Avro.Specific;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace DotNetAtlas.ReliableMessaging.Outbox.EFCore;

/// <summary>
/// Deserializer for Avro messages used by the inbox pattern.
/// Caches deserializers per message type for performance.
/// </summary>
public class AvroDeserializer : IAsyncDeserializer<ISpecificRecord>
{
    private readonly ISchemaRegistryClient _schemaRegistryClient;
    private readonly AvroDeserializerConfig? _avroDeserializerConfig;
    private readonly ConcurrentDictionary<Type, AvroDeserializerWrapper> _deserializersCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AvroDeserializer"/> class.
    /// </summary>
    /// <param name="schemaRegistryClient">The schema registry client.</param>
    /// <param name="avroDeserializerConfig">The Avro deserializer configuration (optional).</param>
    public AvroDeserializer(
        ISchemaRegistryClient schemaRegistryClient,
        AvroDeserializerConfig? avroDeserializerConfig = null)
    {
        _schemaRegistryClient = schemaRegistryClient;
        _avroDeserializerConfig = avroDeserializerConfig;
    }

    /// <summary>
    /// Deserializes Avro bytes to an ISpecificRecord.
    /// </summary>
    /// <param name="data">The data to deserialize.</param>
    /// <param name="isNull">Whether the data is null.</param>
    /// <param name="context">The serialization context.</param>
    /// <returns>The deserialized record.</returns>
    public Task<ISpecificRecord> DeserializeAsync(
        ReadOnlyMemory<byte> data,
        bool isNull,
        SerializationContext context)
    {
        // When implementing IAsyncDeserializer<ISpecificRecord>, we need to know
        // the concrete type at runtime. This requires the caller to use the
        // typed overload or set up the deserializer for a specific type.
        throw new NotSupportedException(
            "Use the Deserialize method with explicit messageType parameter, " +
            "or use AvroDeserializer<T> for compile-time type safety.");
    }

    /// <summary>
    /// Deserializes Avro bytes to a message of the specified runtime type.
    /// </summary>
    /// <param name="data">The data to deserialize.</param>
    /// <param name="messageType">The type of the message to deserialize to.</param>
    /// <param name="isNull">Whether the data is null.</param>
    /// <param name="context">The serialization context.</param>
    /// <returns>The deserialized record.</returns>
    public async Task<ISpecificRecord> DeserializeAsync(
        ReadOnlyMemory<byte> data,
        Type messageType,
        bool isNull,
        SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        var deserializer = _deserializersCache.GetOrAdd(
            messageType,
            t => AvroDeserializerWrapper.Create(t, _schemaRegistryClient, _avroDeserializerConfig));

        return await deserializer.DeserializeAsync(data, isNull, context);
    }
}

internal abstract class AvroDeserializerWrapper
{
    public abstract Task<ISpecificRecord> DeserializeAsync(
        ReadOnlyMemory<byte> data,
        bool isNull,
        SerializationContext context);

    public static AvroDeserializerWrapper Create(
        Type messageType,
        ISchemaRegistryClient schemaRegistryClient,
        AvroDeserializerConfig? avroDeserializerConfig)
    {
        var genericType = typeof(TypedAvroDeserializer<>).MakeGenericType(messageType);
        var deserializerForType =
            (AvroDeserializerWrapper)Activator.CreateInstance(
                genericType, schemaRegistryClient, avroDeserializerConfig)!;

        return deserializerForType;
    }

    private sealed class TypedAvroDeserializer<T> : AvroDeserializerWrapper
        where T : class, ISpecificRecord
    {
        private readonly AvroDeserializer<T> _deserializer;

        public TypedAvroDeserializer(
            ISchemaRegistryClient client,
            AvroDeserializerConfig? config)
        {
            _deserializer = new AvroDeserializer<T>(client, config);
        }

        public override async Task<ISpecificRecord> DeserializeAsync(
            ReadOnlyMemory<byte> data,
            bool isNull,
            SerializationContext context)
        {
            return await _deserializer.DeserializeAsync(data, isNull, context);
        }
    }
}
