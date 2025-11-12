using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay.Config;

/// <summary>
/// Configuration options for the OutboxRelay background service.
/// </summary>
public sealed class OutboxRelayOptions : IValidatableObject
{
    public const string Section = "OutboxRelay";

    /// <summary>
    /// How often to poll for new outbox messages (milliseconds).
    /// </summary>
    [Required]
    [Range(100, 300_000)]
    public required int PollingIntervalMs { get; set; }

    /// <summary>
    /// Maximum number of messages to process in one batch.
    /// </summary>
    [Required]
    [Range(1, 10_000)]
    public required int BatchSize { get; set; }

    /// <summary>
    /// Default Kafka topic to publish outbox messages to when no type-specific mapping is found.
    /// </summary>
    [Required]
    [Length(1, 249)]
    public required string DefaultTopicName { get; set; }

    /// <summary>
    /// Dictionary mapping Avro type names to specific Kafka topics.
    /// Type-specific mappings take precedence over DefaultTopicName.
    /// Key: Avro type name (e.g., "FeedbackChangedEvent")
    /// Value: Kafka topic name.
    /// </summary>
    public Dictionary<string, string> TypeTopicMappings { get; set; } = [];

    /// <summary>
    /// Database schema name where the outbox table is located.
    /// Defaults to "dbo" if not specified.
    /// </summary>
    [Required]
    [RegularExpression("^[a-zA-Z][a-zA-Z0-9_]*$", ErrorMessage = "Schema name must be a valid SQL identifier")]
    [Length(1, 128)]
    public required string SchemaName { get; set; }

    /// <summary>
    /// Database table name for the outbox messages.
    /// Defaults to "OutboxMessages" if not specified.
    /// </summary>
    [Required]
    [RegularExpression("^[a-zA-Z][a-zA-Z0-9_]*$", ErrorMessage = "Table name must be a valid SQL identifier")]
    [Length(1, 128)]
    public required string TableName { get; set; }

    /// <summary>
    /// Timeout for Kafka producer flush operations in milliseconds.
    /// Used for both normal batch processing and graceful shutdown flush.
    /// Must be less than ShutdownTimeoutMs to allow sufficient time for graceful shutdown.
    /// </summary>
    [Range(5000, 120_000)]
    public required int FlushTimeoutMs { get; set; }

    /// <summary>
    /// Total timeout for graceful shutdown in milliseconds.
    /// Defines how long the worker waits for final batch processing and producer flush.
    /// Must be larger than FlushTimeoutMs to allow for cleanup.
    /// </summary>
    [Range(10_000, 180_000)]
    public required int ShutdownTimeoutMs { get; set; }

    public OutboxRelayOptions ShallowClone()
    {
        var clone = (OutboxRelayOptions)MemberwiseClone();
        clone.TypeTopicMappings = new Dictionary<string, string>(TypeTopicMappings);

        return clone;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        if (FlushTimeoutMs >= ShutdownTimeoutMs)
        {
            results.Add(new ValidationResult(
                $"{nameof(FlushTimeoutMs)} ({FlushTimeoutMs}ms) must be less than {nameof(ShutdownTimeoutMs)} " +
                $"({ShutdownTimeoutMs}ms) to allow sufficient time for graceful shutdown operations.",
                [nameof(FlushTimeoutMs), nameof(ShutdownTimeoutMs)]));
        }

        return results;
    }
}
