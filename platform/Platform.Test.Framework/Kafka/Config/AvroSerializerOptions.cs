using System.ComponentModel.DataAnnotations;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace Platform.Test.Framework.Kafka.Config;

/// <summary>
/// Test-framework copy of Avro serializer configuration. See KafkaOptions for why
/// this lives here rather than in a BC tier.
/// </summary>
public sealed class AvroSerializerOptions : AvroSerializerConfig
{
    public const string Section = $"{KafkaOptions.Section}:AvroSerializer";

    [Required]
    public new required SubjectNameStrategy? SubjectNameStrategy { get; set; }

    [Required]
    public new required bool AutoRegisterSchemas { get; set; }
}
