using System.ComponentModel.DataAnnotations;

namespace Platform.Test.Framework.Kafka.Config;

/// <summary>
/// Test-framework copy of the Kafka cluster configuration shape used by every BC.
/// Lives here so Platform.Test.Framework does not need a cross-tier ProjectReference
/// into a specific BC (which broke saga-tier transitive pinning when MassTransit
/// downgraded from v9 — different BC tiers have different transitive graphs).
/// Each BC binds its own typed options from the same configuration keys (Section names
/// match), so this DTO is purely a transport shape inside the test harness.
/// </summary>
public sealed class KafkaOptions
{
    public const string Section = "Kafka";

    [Required]
    [MinLength(1)]
    public required string[] Brokers { get; set; }

    public string BrokersFlat => string.Join(';', Brokers);

    [Required]
    public required SchemaRegistryOptions SchemaRegistry { get; set; }

    [Required]
    public required AvroSerializerOptions AvroSerializer { get; set; }
}
