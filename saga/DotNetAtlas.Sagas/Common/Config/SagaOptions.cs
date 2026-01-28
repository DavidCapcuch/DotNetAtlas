using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Configuration options for saga orchestration.
/// </summary>
public sealed class SagaOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string Section = "Saga";

    /// <summary>
    /// Timeout configuration for the subscription purchase saga.
    /// </summary>
    [Required]
    public required SubscriptionSagaTimeoutOptions SubscriptionTimeouts { get; set; }

    /// <summary>
    /// Timeout configuration for the payment processing saga.
    /// </summary>
    [Required]
    public required PaymentSagaTimeoutOptions PaymentTimeouts { get; set; }

    /// <summary>
    /// Maximum number of retry attempts for saga operations.
    /// </summary>
    [Required]
    [Range(0, 10)]
    public required int MaxRetryAttempts { get; set; }

    /// <summary>
    /// Delay in seconds between retry attempts.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int RetryDelaySeconds { get; set; }

    /// <summary>
    /// Number of concurrent saga instances to process.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    public required int ConcurrencyLimit { get; set; }

    /// <summary>
    /// Kafka bootstrap servers for MassTransit Kafka rider.
    /// </summary>
    [Required]
    public required string KafkaBootstrapServers { get; set; }

    /// <summary>
    /// Schema registry URL for Avro serialization.
    /// </summary>
    [Required]
    public required string SchemaRegistryUrl { get; set; }

    /// <summary>
    /// Topic configuration for saga events.
    /// </summary>
    [Required]
    public required SagaTopicsOptions Topics { get; set; }

    /// <summary>
    /// Consumer group ID for the saga orchestrator.
    /// </summary>
    [Required]
    public string ConsumerGroup { get; set; } = "saga-orchestrator";
}
