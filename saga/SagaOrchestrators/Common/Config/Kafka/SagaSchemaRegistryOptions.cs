using System.ComponentModel.DataAnnotations;
using Confluent.SchemaRegistry;

namespace SagaOrchestrators.Common.Config.Kafka;

/// <summary>
/// Schema Registry connection configuration for saga Kafka consumers.
/// </summary>
public sealed class SagaSchemaRegistryOptions : SchemaRegistryConfig
{
    public const string Section = $"{KafkaOptions.Section}:SchemaRegistry";

    /// <summary>
    /// Schema Registry URL (e.g., "http://localhost:8081").
    /// </summary>
    [Required]
    [Url]
    public new required string Url { get; set; }
}
