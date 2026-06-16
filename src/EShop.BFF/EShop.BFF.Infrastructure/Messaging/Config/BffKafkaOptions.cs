using System.ComponentModel.DataAnnotations;

namespace EShop.BFF.Infrastructure.Messaging.Config;

/// <summary>
/// Kafka cluster configuration for the BFF's cache-invalidation consumer (brokers + schema registry).
/// Bound from section <c>Kafka</c>. No Avro <em>serializer</em> options — the BFF only consumes (it
/// deserializes incoming Avro; it never produces), per bff.md § 2.2.
/// </summary>
public sealed class BffKafkaOptions
{
    public const string Section = "Kafka";

    [Required]
    [MinLength(1)]
    public required string[] Brokers { get; set; }

    [Required]
    public required BffSchemaRegistryOptions SchemaRegistry { get; set; }
}

/// <summary>Schema Registry endpoint for Avro deserialization. Bound from <c>Kafka:SchemaRegistry</c>.</summary>
public sealed class BffSchemaRegistryOptions
{
    [Required]
    [Url]
    public required string Url { get; set; }
}
