using System.Collections.Concurrent;
using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace DotNetAtlas.Avro.UniversalSerDes;

/// <summary>
/// Serializer for Avro messages used by the outbox pattern.
/// Caches serializers per message type for performance.
/// </summary>
public class UniversalAvroSerializer : ISerializer<ISpecificRecord>
{
    private readonly ISchemaRegistryClient _schemaRegistryClient;
    private readonly AvroSerializerConfig _avroSerializerConfig;
    private readonly ConcurrentDictionary<Type, AvroSerializerWrapper> _serializersCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="UniversalAvroSerializer"/> class.
    /// </summary>
    /// <param name="schemaRegistryClient">The schema registry client.</param>
    /// <param name="avroSerializerOptions">The Avro serializer configuration.</param>
    public UniversalAvroSerializer(
        ISchemaRegistryClient schemaRegistryClient,
        AvroSerializerConfig avroSerializerOptions)
    {
        _schemaRegistryClient = schemaRegistryClient;
        _avroSerializerConfig = avroSerializerOptions;
    }

    public byte[] Serialize(ISpecificRecord data, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(data);

        var messageType = data.GetType();
        var serializer = _serializersCache.GetOrAdd(
            messageType,
            t => AvroSerializerWrapper.Create(t, _schemaRegistryClient, _avroSerializerConfig));
        return serializer.Serialize(data, context);
    }
}

internal abstract class AvroSerializerWrapper
{
    public abstract byte[] Serialize(ISpecificRecord message, SerializationContext context);

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

        public override byte[] Serialize(ISpecificRecord message, SerializationContext context)
        {
            return _serializer.Serialize((T)message, context);
        }
    }
}
