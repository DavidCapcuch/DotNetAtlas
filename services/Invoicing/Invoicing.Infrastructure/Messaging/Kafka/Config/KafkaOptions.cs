using System.ComponentModel.DataAnnotations;

namespace Invoicing.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Kafka cluster infrastructure configuration (brokers + schema-registry +
/// avro serializer). Bound from section <c>Kafka</c>. Mirrors the shape used
/// across Catalog / Ordering / Inventory.
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
