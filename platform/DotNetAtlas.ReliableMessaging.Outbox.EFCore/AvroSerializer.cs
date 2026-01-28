using System.Collections.Concurrent;
using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace DotNetAtlas.ReliableMessaging.Outbox.EFCore;

/// <summary>
/// Serializer for Avro messages used by the outbox pattern.
/// Caches serializers per message type for performance.
/// </summary>
public class AvroSerializer
{
    private readonly ISchemaRegistryClient _schemaRegistryClient;
    private readonly AvroSerializerConfig _avroSerializerConfig;
    private readonly ConcurrentDictionary<Type, AvroSerializerWrapper> _serializersCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AvroSerializer"/> class.
    /// </summary>
    /// <param name="schemaRegistryClient">The schema registry client.</param>
    /// <param name="avroSerializerOptions">The Avro serializer configuration.</param>
    public AvroSerializer(
        ISchemaRegistryClient schemaRegistryClient,
        AvroSerializerConfig avroSerializerOptions)
    {
        _schemaRegistryClient = schemaRegistryClient;
        _avroSerializerConfig = avroSerializerOptions;
    }

    /// <summary>
    /// Serializes an Avro message to bytes.
    /// </summary>
    /// <param name="message">The message to serialize.</param>
    /// <param name="messageType">The type of the message.</param>
    /// <returns>The serialized bytes.</returns>
    public byte[] Serialize(ISpecificRecord message, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(message);

        var serializer = _serializersCache.GetOrAdd(
            messageType,
            t => AvroSerializerWrapper.Create(t, _schemaRegistryClient, _avroSerializerConfig));

        return serializer.Serialize(message);
    }
}

internal abstract class AvroSerializerWrapper
{
    public abstract byte[] Serialize(ISpecificRecord message);

    public static AvroSerializerWrapper Create(
        Type messageType,
        ISchemaRegistryClient schemaRegistryClient,
        AvroSerializerConfig avroSerializerConfig)
    {
        var genericType = typeof(TypedAvroSerializer<>).MakeGenericType(messageType);
        var serializerForType =
            (AvroSerializerWrapper)Activator.CreateInstance(genericType, schemaRegistryClient, avroSerializerConfig)!;

        return serializerForType;
    }

    private sealed class TypedAvroSerializer<T> : AvroSerializerWrapper
        where T : ISpecificRecord
    {
        private readonly ISerializer<T> _serializer;

        public TypedAvroSerializer(ISchemaRegistryClient client, AvroSerializerConfig config)
        {
            _serializer = new AvroSerializer<T>(client, config).AsSyncOverAsync();
        }

        public override byte[] Serialize(ISpecificRecord message)
        {
            return _serializer.Serialize((T)message, SerializationContext.Empty);
        }
    }
}
