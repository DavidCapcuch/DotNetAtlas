using System.ComponentModel.DataAnnotations;
using Confluent.SchemaRegistry;

namespace DotNetAtlas.Sagas.Common.Config;

/// <summary>
/// Schema Registry connection configuration for saga Kafka consumers.
/// </summary>
public sealed class SagaSchemaRegistryOptions : SchemaRegistryConfig
{
    /// <summary>
    /// Schema Registry URL (e.g., "http://localhost:8081").
    /// </summary>
    [Required]
    [Url]
    public new required string Url { get; set; }
}
