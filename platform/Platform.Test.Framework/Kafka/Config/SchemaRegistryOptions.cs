using System.ComponentModel.DataAnnotations;
using Confluent.SchemaRegistry;

namespace Platform.Test.Framework.Kafka.Config;

/// <summary>
/// Test-framework copy of Schema Registry connection configuration. See KafkaOptions
/// for why this lives here rather than in a BC tier.
/// </summary>
public sealed class SchemaRegistryOptions : SchemaRegistryConfig
{
    public const string Section = $"{KafkaOptions.Section}:SchemaRegistry";

    [Required]
    [Url]
    public new required string Url { get; set; }
}
