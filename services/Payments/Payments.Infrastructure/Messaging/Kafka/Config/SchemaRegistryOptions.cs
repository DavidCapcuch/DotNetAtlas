using System.ComponentModel.DataAnnotations;
using Confluent.SchemaRegistry;

namespace Payments.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Schema Registry connection configuration.
/// </summary>
public sealed class SchemaRegistryOptions : SchemaRegistryConfig
{
    public const string Section = $"{KafkaOptions.Section}:SchemaRegistry";

    [Required]
    [Url]
    public new required string Url { get; set; }
}
