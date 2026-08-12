using Confluent.SchemaRegistry.Serdes;
using Microsoft.Extensions.Options;

namespace SagaOrchestrators.Common.Config.Kafka;

/// <summary>
/// Avro deserializer configuration for saga Kafka consumers.
/// Inherits from AvroDeserializerConfig to expose all Confluent Avro deserialization settings.
/// </summary>
/// <remarks>
/// Recommended read: https://docs.confluent.io/platform/current/schema-registry/fundamentals/serdes-develop/serdes-avro.html.
/// <para>
/// The whole instance is handed to <c>UniversalAvroDeserializer</c>, which reads the base string
/// dictionary rather than the CLR properties — which is why every setting is bound by its
/// <see cref="AvroDeserializerConfig"/> property name and none is redeclared here. Redeclaring one
/// with <c>new</c> writes a CLR backing field instead; the reflection binder populates the shadow
/// and the hidden base property alike, so the values do still arrive — until a binder that reads
/// only declared members (the configuration-binding source generator, trimming, AOT) leaves that
/// dictionary empty. <c>UseLatestVersion</c> is one such setting: when false, the schema id embedded
/// in the message is used.
/// </para>
/// </remarks>
public sealed class AvroDeserializerOptions : AvroDeserializerConfig
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string Section = $"{KafkaOptions.Section}:AvroDeserializer";
}

/// <summary>
/// Startup validation for <see cref="AvroDeserializerOptions"/>. The subject name strategy
/// (<c>Topic</c>, <c>Record</c>, <c>TopicRecord</c> — <c>Record</c> for the saga's multi-type topics)
/// decides which schema each message resolves against, and lives on the
/// <see cref="AvroDeserializerConfig"/> base where a data annotation cannot reach it without the
/// <c>new</c> redeclaration this type deliberately does not carry.
/// </summary>
internal sealed class AvroDeserializerOptionsValidator : IValidateOptions<AvroDeserializerOptions>
{
    public ValidateOptionsResult Validate(string? name, AvroDeserializerOptions options) =>
        options.SubjectNameStrategy is null
            ? ValidateOptionsResult.Fail(
                $"Avro deserializer configuration error in section '{AvroDeserializerOptions.Section}': " +
                $"{nameof(options.SubjectNameStrategy)} is required.")
            : ValidateOptionsResult.Success;
}
