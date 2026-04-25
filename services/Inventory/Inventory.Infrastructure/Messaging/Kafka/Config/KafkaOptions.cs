using System.ComponentModel.DataAnnotations;

namespace Inventory.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Kafka cluster infrastructure configuration (brokers + schema-registry +
/// avro serializer). Bound from section <c>Kafka</c>. M4 only exposes the
/// producer-side plumbing required by the outbox relay; the consumer-side
/// settings for the saga-command intake + Catalog inbox land in M5.
/// </summary>
public sealed class KafkaOptions
{
    public const string Section = "Kafka";

    [Required]
    [MinLength(1)]
    public required string[] Brokers { get; set; }

    public string BrokersFlat => string.Join(';', Brokers);

    [Required]
    public required SchemaRegistryOptions SchemaRegistry { get; set; }

    [Required]
    public required AvroSerializerOptions AvroSerializer { get; set; }
}
