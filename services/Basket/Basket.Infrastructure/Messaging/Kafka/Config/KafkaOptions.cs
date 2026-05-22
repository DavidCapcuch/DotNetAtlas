using System.ComponentModel.DataAnnotations;

namespace Basket.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Kafka cluster infrastructure configuration (brokers + schema-registry +
/// avro serializer). Bound from section <c>Kafka</c>. Today only the
/// producer-side plumbing required by <c>outbox-relay-basket</c> is exposed;
/// consumer wiring (e.g. a Catalog-invalidation inbox consumer) can be added
/// later if and when basket.md adopts one.
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
