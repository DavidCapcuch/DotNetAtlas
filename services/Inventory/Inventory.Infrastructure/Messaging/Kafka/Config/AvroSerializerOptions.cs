using Confluent.SchemaRegistry.Serdes;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Messaging.Kafka.Config;

/// <summary>
/// Avro serializer configuration. Inherits from <see cref="AvroSerializerConfig"/>
/// to expose all Confluent serializer settings.
/// </summary>
/// <remarks>
/// The whole instance is handed to KafkaFlow's <c>AddSchemaRegistryAvroSerializer</c>, which reads
/// the base string dictionary rather than the CLR properties — which is why every setting is bound
/// by its <see cref="AvroSerializerConfig"/> property name and none is redeclared here. Redeclaring
/// one with <c>new</c> writes a CLR backing field instead; the reflection binder populates the
/// shadow and the hidden base property alike, so the values do still arrive — until a binder that
/// reads only declared members (the configuration-binding source generator, trimming, AOT) leaves
/// that dictionary empty.
/// </remarks>
public sealed class AvroSerializerOptions : AvroSerializerConfig
{
    public const string Section = $"{KafkaOptions.Section}:AvroSerializer";
}

/// <summary>
/// Startup validation for <see cref="AvroSerializerOptions"/>. Both settings live on the
/// <see cref="AvroSerializerConfig"/> base, where a data annotation cannot reach them without the
/// <c>new</c> redeclaration this type deliberately does not carry. Neither may be left unstated:
/// the serializer's subject naming decides which schema a consumer resolves, and auto-registration
/// decides whether an unknown schema is created or refused — both silently wrong under a default
/// nobody chose.
/// </summary>
internal sealed class AvroSerializerOptionsValidator : IValidateOptions<AvroSerializerOptions>
{
    public ValidateOptionsResult Validate(string? name, AvroSerializerOptions options)
    {
        List<string> failures = [];

        if (options.SubjectNameStrategy is null)
        {
            failures.Add(Missing(nameof(options.SubjectNameStrategy)));
        }

        if (options.AutoRegisterSchemas is null)
        {
            failures.Add(Missing(nameof(options.AutoRegisterSchemas)));
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    private static string Missing(string setting) =>
        $"Avro serializer configuration error in section '{AvroSerializerOptions.Section}': {setting} is required.";
}
