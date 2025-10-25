using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace DotNetAtlas.Infrastructure.Messaging.Kafka.WeatherForecastEvents;

/// <summary>
/// Kafka producer configuration for forecast events.
/// Inherits from ProducerConfig to expose all Confluent.Kafka producer settings.
/// </summary>
/// <remarks>
/// Recommended read: https://github.com/confluentinc/confluent-kafka-dotnet/wiki/Producer.
/// </remarks>
public sealed class KafkaForecastEventsProducerOptions : ProducerConfig
{
    public const string Section = "KafkaForecastEventsProducer";

    /// <summary>
    /// Client ID for identifying the producer.
    /// </summary>
    [Required]
    public new required string ClientId { get; set; }

    /// <summary>
    /// Compression algorithm for messages.
    /// Options: None, Gzip, Snappy, Lz4, Zstd.
    /// </summary>
    [Required]
    public new required CompressionType? CompressionType { get; set; }

    /// <summary>
    /// Number of acknowledgments the producer requires before considering a request complete.
    /// Options: None (0), Leader (1), All (-1).
    /// </summary>
    [Required]
    public new required Acks? Acks { get; set; }

    /// <summary>
    /// Enable idempotent producer (exactly-once semantics).
    /// </summary>
    [Required]
    public new required bool? EnableIdempotence { get; set; }
}
