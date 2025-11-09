using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay.Config;

/// <summary>
/// Kafka producer configuration for outbox relay.
/// Inherits from ProducerConfig to expose all Confluent.Kafka producer settings.
/// </summary>
/// <remarks>
/// Recommended read: https://github.com/confluentinc/confluent-kafka-dotnet/wiki/Producer.
/// </remarks>
public sealed class KafkaProducerOptions : ProducerConfig
{
    public const string Section = "KafkaProducer";

    /// <summary>
    /// Client ID for identifying the producer.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public new required string ClientId { get; set; }

    /// <summary>
    /// Kafka Bootstrap Servers.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public new required string BootstrapServers { get; set; }

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

    /// <summary>
    /// Maximum number of in-flight requests per connection.
    /// Default: 5.
    /// </summary>
    public new int? MaxInFlight { get; set; }

    /// <summary>
    /// Time to wait before sending messages to batch them together (milliseconds).
    /// Default: 5.
    /// </summary>
    public new int? LingerMs { get; set; }

    /// <summary>
    /// Maximum size of a single batch (bytes).
    /// </summary>
    public new int? BatchSize { get; set; }

    /// <summary>
    /// Local message timeout (milliseconds).
    /// Default: 300000 (5 minutes).
    /// </summary>
    public new int? MessageTimeoutMs { get; set; }

    /// <summary>
    /// Request timeout for broker communication (milliseconds).
    /// Default: 30000 (30 seconds).
    /// </summary>
    public new int? RequestTimeoutMs { get; set; }

    /// <summary>
    /// Maximum number of times to retry sending a message.
    /// Default: 10.
    /// </summary>
    public new int? MessageSendMaxRetries { get; set; }

    /// <summary>
    /// Backoff time between retries (milliseconds).
    /// Default: 100.
    /// </summary>
    public new int? RetryBackoffMs { get; set; }

    public KafkaProducerOptions ShallowClone() => (KafkaProducerOptions)MemberwiseClone();
}
