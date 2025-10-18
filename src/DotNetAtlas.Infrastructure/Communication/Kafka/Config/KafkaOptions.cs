using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Infrastructure.Communication.Kafka.Config;

/// <summary>
/// Kafka cluster infrastructure configuration options.
/// </summary>
public sealed class KafkaOptions
{
    public const string Section = "Kafka";

    /// <summary>
    /// Kafka broker addresses (e.g., ["localhost:9094"]).
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string[] Brokers { get; set; }

    public string BrokersFlat => string.Join(';', Brokers);

    /// <summary>
    /// Schema Registry connection configuration.
    /// </summary>
    [Required]
    public required SchemaRegistryOptions SchemaRegistry { get; set; }

    /// <summary>
    /// Avro serializer configuration.
    /// </summary>
    [Required]
    public required AvroSerializerOptions AvroSerializer { get; set; }
}
