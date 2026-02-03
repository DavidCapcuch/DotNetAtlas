using System.ComponentModel.DataAnnotations;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Avro deserializer configuration for saga Kafka consumers.
/// Inherits from AvroDeserializerConfig to expose all Confluent Avro deserialization settings.
/// </summary>
/// <remarks>
/// Recommended read: https://docs.confluent.io/platform/current/schema-registry/fundamentals/serdes-develop/serdes-avro.html.
/// </remarks>
public sealed class AvroDeserializerOptions : AvroDeserializerConfig
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string Section = $"{SagaOptions.Section}:AvroDeserializer";

    /// <summary>
    /// Subject name strategy for schema lookup.
    /// Options: Topic, Record, TopicRecord.
    /// Default: Record (recommended for multi-type topics).
    /// </summary>
    [Required]
    public new required SubjectNameStrategy? SubjectNameStrategy { get; set; }

    /// <summary>
    /// Whether to use the latest schema version from the registry.
    /// When false, uses the schema ID embedded in the message.
    /// Default: false (recommended for backward compatibility).
    /// </summary>
    [Required]
    public new required bool? UseLatestVersion { get; set; }
}
