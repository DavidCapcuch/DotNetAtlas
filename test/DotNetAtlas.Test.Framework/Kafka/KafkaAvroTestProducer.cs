using Avro.Specific;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using DotNetAtlas.Avro.UniversalSerDes;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;

namespace DotNetAtlas.Test.Framework.Kafka;

/// <summary>
/// Kafka test producer for integration tests that produces Avro-serialized messages.
/// Uses the Confluent Schema Registry for schema management.
/// </summary>
public sealed class KafkaAvroTestProducer : IDisposable
{
    private readonly IProducer<string, ISpecificRecord> _producer;
    private readonly CachedSchemaRegistryClient _schemaRegistryClient;

    public KafkaAvroTestProducer(KafkaOptions kafkaOptions)
    {
        _schemaRegistryClient = new CachedSchemaRegistryClient(new SchemaRegistryConfig
        {
            Url = kafkaOptions.SchemaRegistry.Url
        });

        var avroSerializerOptions = new AvroSerializerConfig
        {
            AutoRegisterSchemas = true,
            SubjectNameStrategy = SubjectNameStrategy.Record,
            NormalizeSchemas = true
        };
        _producer = new ProducerBuilder<string, ISpecificRecord>(new ProducerConfig
            {
                BootstrapServers = kafkaOptions.BrokersFlat
            })
            .SetValueSerializer(new UniversalAvroSerializer(_schemaRegistryClient, avroSerializerOptions))
            .Build();
    }

    /// <summary>
    /// Produces an Avro-serialized message to the specified Kafka topic.
    /// </summary>
    /// <param name="topic">The Kafka topic to produce to.</param>
    /// <param name="key">The message key (typically a correlation or user ID).</param>
    /// <param name="value">The Avro message value.</param>
    public async Task ProduceAsync(string topic, Guid key, ISpecificRecord value)
    {
        await ProduceAsync(topic, key.ToString(), value);
    }

    /// <summary>
    /// Produces an Avro-serialized message to the specified Kafka topic.
    /// </summary>
    /// <param name="topic">The Kafka topic to produce to.</param>
    /// <param name="key">The message key (typically a correlation or user ID).</param>
    /// <param name="value">The Avro message value.</param>
    public async Task ProduceAsync(string topic, string key, ISpecificRecord value)
    {
        await _producer.ProduceAsync(topic, new Message<string, ISpecificRecord>
        {
            Key = key,
            Value = value
        });
    }

    public void Dispose()
    {
        _producer.Dispose();
        _schemaRegistryClient.Dispose();
    }
}
