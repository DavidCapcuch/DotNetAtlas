using System.ComponentModel.DataAnnotations;
using Confluent.SchemaRegistry;

namespace DotNetAtlas.Infrastructure.Communication.Kafka.Config;

/// <summary>
/// Schema Registry connection configuration.
/// </summary>
public sealed class SchemaRegistryOptions : SchemaRegistryConfig
{
    public const string Section = $"{KafkaOptions.Section}:SchemaRegistry";

    /// <summary>
    /// Schema Registry URL (e.g., "http://localhost:8081").
    /// </summary>
    [Required]
    [Url]
    public new required string Url { get; set; }
}
