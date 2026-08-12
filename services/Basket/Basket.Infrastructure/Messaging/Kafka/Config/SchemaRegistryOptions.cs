using Confluent.SchemaRegistry;
using Microsoft.Extensions.Options;

namespace Basket.Infrastructure.Messaging.Kafka.Config;

/// <summary>Schema Registry connection configuration.</summary>
/// <remarks>
/// Every Schema Registry setting is bindable by its <see cref="SchemaRegistryConfig"/> property name
/// without being redeclared here. Redeclaring one with <c>new</c> writes a CLR backing field, whereas
/// <see cref="CachedSchemaRegistryClient"/> enumerates the base string dictionary; the reflection
/// binder populates the shadow and the hidden base property alike, so the values do still arrive —
/// until a binder that reads only declared members (the configuration-binding source generator,
/// trimming, AOT) leaves that dictionary empty.
/// </remarks>
public sealed class SchemaRegistryOptions : SchemaRegistryConfig
{
    public const string Section = $"{KafkaOptions.Section}:SchemaRegistry";
}

/// <summary>
/// Startup validation for <see cref="SchemaRegistryOptions"/>. <c>Url</c> lives on the
/// <see cref="SchemaRegistryConfig"/> base, where a data annotation cannot reach it without the
/// <c>new</c> redeclaration this type deliberately does not carry.
/// </summary>
internal sealed class SchemaRegistryOptionsValidator : IValidateOptions<SchemaRegistryOptions>
{
    /// <summary>
    /// The schemes <c>[Url]</c> accepts — its whole check is this prefix test, so keeping the list
    /// verbatim is what makes the move off the annotation behaviour-preserving.
    /// </summary>
    private static readonly string[] UrlSchemes = ["http://", "https://", "ftp://"];

    public ValidateOptionsResult Validate(string? name, SchemaRegistryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            return Fail($"{nameof(options.Url)} is required.");
        }

        return UrlSchemes.Any(scheme => options.Url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            ? ValidateOptionsResult.Success
            : Fail($"{nameof(options.Url)} must start with one of: {string.Join(", ", UrlSchemes)}.");
    }

    private static ValidateOptionsResult Fail(string problem) =>
        ValidateOptionsResult.Fail(
            $"Schema Registry configuration error in section '{SchemaRegistryOptions.Section}': {problem}");
}
