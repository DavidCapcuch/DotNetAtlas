using System.ComponentModel.DataAnnotations;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace DotNetAtlas.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Avro serializer configuration.
/// Inherits from AvroSerializerConfig to expose all Avro serialization settings.
/// </summary>
public sealed class AvroSerializerOptions : AvroSerializerConfig
{
    public const string Section = $"{KafkaOptions.Section}:AvroSerializer";

    /// <summary>
    /// Subject name strategy for schema registration.
    /// Options: TopicName, RecordName, TopicRecord.
    /// </summary>
    [Required]
    public new required SubjectNameStrategy? SubjectNameStrategy { get; set; }

    /// <summary>
    /// Whether to automatically register schemas with the registry.
    /// Use only in the local environment, nowhere else.
    /// </summary>
    [Required]
    public new required bool AutoRegisterSchemas { get; set; }
}
