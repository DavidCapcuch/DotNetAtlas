using System.ComponentModel.DataAnnotations;
using Confluent.SchemaRegistry;

namespace Platform.Test.Framework.Kafka.Config;

/// <summary>
/// Test-framework copy of Avro serializer configuration. See KafkaOptions for why
/// this lives here rather than in a BC tier.
/// </summary>
/// <remarks>
/// A plain POCO, unlike each BC's counterpart, which derives from
/// <c>Confluent.SchemaRegistry.Serdes.AvroSerializerConfig</c>. Nothing here is ever handed to a
/// Confluent serializer — <c>KafkaTestProducer</c> builds its own config — so this only ever carries
/// values to <c>WebHostBuilderExtensions.UseKafkaSettings</c>, which pushes them into the host under
/// this section. Deriving from the vendor type would move them into a string dictionary nothing
/// reads, and invite the property-shadowing trap for no gain.
/// </remarks>
public sealed class AvroSerializerOptions
{
    public const string Section = $"{KafkaOptions.Section}:AvroSerializer";

    [Required]
    public required SubjectNameStrategy? SubjectNameStrategy { get; set; }

    [Required]
    public required bool AutoRegisterSchemas { get; set; }
}
