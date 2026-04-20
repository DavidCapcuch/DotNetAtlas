using System.Text.Json;

namespace Platform.ServiceDefaults.Json;

/// <summary>
/// Applies the platform JSON conventions (ADR-0015 ISO 8601 with offset) to a
/// <see cref="JsonSerializerOptions"/> instance. Intended to be composed into whichever endpoint
/// framework a BC chooses (FastEndpoints, MVC, minimal APIs, outbox payloads), so Wave 0 does not
/// silently override a consumer's existing JSON configuration.
/// </summary>
public static class PlatformJsonOptions
{
    /// <summary>
    /// Adds the <see cref="JsonDateTimeOffsetConverter"/> to <paramref name="options"/> if one is not
    /// already present. Returns the same options instance for fluent composition.
    /// </summary>
    public static JsonSerializerOptions ConfigurePlatformJsonOptions(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var existing in options.Converters)
        {
            if (existing is JsonDateTimeOffsetConverter)
            {
                return options;
            }
        }

        options.Converters.Add(new JsonDateTimeOffsetConverter());
        return options;
    }
}
