using System.ComponentModel.DataAnnotations;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace Catalog.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Avro serializer configuration. Inherits from <see cref="AvroSerializerConfig"/>
/// to expose all Confluent serializer settings.
/// </summary>
public sealed class AvroSerializerOptions : AvroSerializerConfig
{
    public const string Section = $"{KafkaOptions.Section}:AvroSerializer";

    [Required]
    public new required SubjectNameStrategy? SubjectNameStrategy { get; set; }

    [Required]
    public new required bool AutoRegisterSchemas { get; set; }
}
