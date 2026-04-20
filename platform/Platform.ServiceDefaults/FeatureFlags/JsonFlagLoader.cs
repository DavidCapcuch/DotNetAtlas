using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenFeature.Providers.Memory;

namespace Platform.ServiceDefaults.FeatureFlags;

/// <summary>
/// Hydrates an OpenFeature <see cref="InMemoryProvider"/> snapshot from the <c>flags.json</c>
/// file seeded in Wave 0 M7 (ADR-0014 schema).
/// </summary>
/// <remarks>
/// v1 supports boolean variants only; string / integer / double variants are deferred — the
/// loader ignores non-boolean flags with a warning so the missing-file path and a partially-
/// understood file path stay boring (startup never throws on flag config).
/// </remarks>
public static class JsonFlagLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Loads the flag file at <paramref name="filePath"/>, returning a dictionary keyed by flag
    /// name ready to hand to <see cref="InMemoryProvider"/>. Missing files produce an empty
    /// dictionary and a debug log; malformed files produce an empty dictionary and a warning.
    /// </summary>
    public static IDictionary<string, Flag> Load(string filePath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            logger?.LogDebug("Feature-flags file {FilePath} not found; starting with empty flag set.", filePath);
            return new Dictionary<string, Flag>(StringComparer.Ordinal);
        }

        FlagFileDocument? doc;
        try
        {
            using var stream = File.OpenRead(filePath);
            doc = JsonSerializer.Deserialize<FlagFileDocument>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Feature-flags file {FilePath} is malformed; starting with empty flag set.", filePath);
            return new Dictionary<string, Flag>(StringComparer.Ordinal);
        }

        var flags = new Dictionary<string, Flag>(StringComparer.Ordinal);
        if (doc?.Flags is null)
        {
            return flags;
        }

        foreach (var (key, entry) in doc.Flags)
        {
            if (entry is null || entry.Variants is null || string.IsNullOrWhiteSpace(entry.DefaultVariant))
            {
                logger?.LogWarning("Flag '{Key}' is missing variants or defaultVariant; skipped.", key);
                continue;
            }

            if (TryBuildBooleanFlag(entry, out var flag))
            {
                flags[key] = flag!;
                continue;
            }

            logger?.LogWarning(
                "Flag '{Key}' has non-boolean variants; only boolean flags are supported in Wave 0 v1. Skipped.",
                key);
        }

        return flags;
    }

    private static bool TryBuildBooleanFlag(FlagFileEntry entry, out Flag? flag)
    {
        var booleanVariants = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (variantKey, element) in entry.Variants!)
        {
            if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                flag = null;
                return false;
            }

            booleanVariants[variantKey] = element.GetBoolean();
        }

        if (!booleanVariants.ContainsKey(entry.DefaultVariant!))
        {
            flag = null;
            return false;
        }

        flag = new Flag<bool>(booleanVariants, entry.DefaultVariant!);
        return true;
    }

    private sealed class FlagFileDocument
    {
        [JsonPropertyName("flags")]
        public Dictionary<string, FlagFileEntry>? Flags { get; set; }
    }

    private sealed class FlagFileEntry
    {
        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("variants")]
        public Dictionary<string, JsonElement>? Variants { get; set; }

        [JsonPropertyName("defaultVariant")]
        public string? DefaultVariant { get; set; }
    }
}
