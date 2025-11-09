using System.Collections.Concurrent;
using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace DotNetAtlas.Outbox.EntityFrameworkCore.Core;

public class AvroSerializer
{
    private readonly ISchemaRegistryClient _schemaRegistryClient;
    private readonly AvroSerializerConfig _avroSerializerConfig;
    private readonly ConcurrentDictionary<Type, AvroSerializerWrapper> _serializersCache = new();

    public AvroSerializer(
        ISchemaRegistryClient schemaRegistryClient,
        AvroSerializerConfig avroSerializerOptions)
    {
        _schemaRegistryClient = schemaRegistryClient;
        _avroSerializerConfig = avroSerializerOptions;
    }

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
