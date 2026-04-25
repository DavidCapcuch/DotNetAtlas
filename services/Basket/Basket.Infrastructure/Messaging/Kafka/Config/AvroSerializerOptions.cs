using System.ComponentModel.DataAnnotations;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace Basket.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Avro serializer configuration. Inherits from <see cref="AvroSerializerConfig"/>
/// to expose all Confluent serializer settings. The outbox publisher uses this
/// to produce the Avro payload written to <c>basket.OutboxMessages.avro_payload</c>
/// before the relay lifts it onto the <c>basket.sessions</c> topic.
/// </summary>
public sealed class AvroSerializerOptions : AvroSerializerConfig
{
    public const string Section = $"{KafkaOptions.Section}:AvroSerializer";

    [Required]
    public new required SubjectNameStrategy? SubjectNameStrategy { get; set; }

    [Required]
    public new required bool AutoRegisterSchemas { get; set; }
}
