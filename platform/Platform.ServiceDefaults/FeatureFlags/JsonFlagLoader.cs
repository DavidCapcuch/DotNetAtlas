using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenFeature.Providers.Memory;

namespace Platform.ServiceDefaults.FeatureFlags;

/// <summary>
/// Hydrates an OpenFeature <see cref="InMemoryProvider"/> snapshot from the <c>flags.json</c>
/// file (ADR-0014 schema).
/// </summary>
/// <remarks>
/// v1 supports boolean variants only; string / integer / double variants are deferred — the
/// loader ignores non-boolean flags with a warning so the missing-file path and a partially-
/// understood file path stay boring. Startup never throws on flag *content*; a blank
/// <see cref="FeatureFlagsOptions.FilePath"/> is a misconfigured service and still throws.
/// <para>
/// A flag whose <c>state</c> is not <c>ENABLED</c> is still loaded, but disabled, so it resolves
/// to the call site's <c>defaultValue</c> with reason <c>DISABLED</c> rather than to an error —
/// which keeps a deliberate switch-off distinguishable from a broken config in telemetry. That
/// default is not necessarily the feature's "off" state: a kill switch whose call site defaults to
/// <c>true</c> keeps running when its flag is disabled.
/// </para>
/// <para>
/// An unrecognised state <em>value</em> counts as disabled, so a typo fails toward the call site's
/// default rather than toward serving a flag someone meant to switch off. An absent <c>state</c>
/// property means enabled, so a misspelled property name is not covered by that fail-safe —
/// rejecting unknown properties instead would fail the whole document and empty every flag.
/// </para>
/// </remarks>
public static class JsonFlagLoader
{
    private const string EnabledState = "ENABLED";
    private const string DisabledState = "DISABLED";

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

            if (TryBuildBooleanFlag(entry, key, logger, out var flag))
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

    private static bool IsDisabled(string? state, string key, ILogger? logger)
    {
        if (state is null || state.Equals(EnabledState, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (state.Equals(DisabledState, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        logger?.LogWarning("Flag '{Key}' has unrecognised state '{State}'; treated as DISABLED.", key, state);
        return true;
    }

    private static bool TryBuildBooleanFlag(FlagFileEntry entry, string key, ILogger? logger, out Flag? flag)
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

        // State is read last, so a flag that is skipped as malformed never also logs a state warning.
        flag = new Flag<bool>(
            booleanVariants,
            entry.DefaultVariant!,
            disabled: IsDisabled(entry.State, key, logger));
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
