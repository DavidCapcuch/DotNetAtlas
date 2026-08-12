using System.ComponentModel.DataAnnotations;

namespace Platform.Test.Framework.Kafka.Config;

/// <summary>
/// Test-framework copy of Schema Registry connection configuration. See KafkaOptions
/// for why this lives here rather than in a BC tier.
/// </summary>
/// <remarks>
/// A plain POCO, unlike each BC's counterpart, which derives from
/// <c>Confluent.SchemaRegistry.SchemaRegistryConfig</c>. Nothing here is ever handed to a Confluent
/// client — <c>KafkaTestProducer</c> and <c>KafkaTestConsumer</c> build their own configs — so this
/// only ever carries the container's URL to <c>WebHostBuilderExtensions.UseKafkaSettings</c>, which
/// pushes it into the host under this section. Deriving from the vendor type would move that value
/// into a string dictionary nothing reads, and invite the property-shadowing trap for no gain.
/// </remarks>
public sealed class SchemaRegistryOptions
{
    public const string Section = $"{KafkaOptions.Section}:SchemaRegistry";

    [Required]
    [Url]
    public required string Url { get; set; }
}
