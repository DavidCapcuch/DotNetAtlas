using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Kafka infrastructure configuration options for saga consumers.
/// </summary>
public sealed class SagaKafkaOptions
{
    public const string Section = "Kafka";

    /// <summary>
    /// Kafka broker addresses (e.g., ["localhost:9094"]).
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string[] Brokers { get; set; }

    /// <summary>
    /// Returns brokers as a semicolon-separated string for Kafka client configuration.
    /// </summary>
    public string BrokersFlat => string.Join(';', Brokers);

    /// <summary>
    /// Schema Registry connection configuration.
    /// </summary>
    [Required]
    public required SagaSchemaRegistryOptions SchemaRegistry { get; set; }

    /// <summary>
    /// Topic configuration for saga events.
    /// </summary>
    [Required]
    public required SagaTopicsOptions Topics { get; set; }

    /// <summary>
    /// Consumer groups configuration for saga events.
    /// </summary>
    [Required]
    public required SagaConsumerGroupsOptions ConsumerGroups { get; set; }

    /// <summary>
    /// Avro deserializer configuration for Kafka consumers.
    /// </summary>
    [Required]
    public required AvroDeserializerOptions AvroDeserializer { get; set; }
}
