namespace Platform.ServiceDefaults.FeatureFlags;

/// <summary>
/// Options for OpenFeature + JSON-file provider wiring (ADR-0014).
/// </summary>
public sealed class FeatureFlagsOptions
{
    /// <summary>Configuration section name: <c>FeatureFlags</c>.</summary>
    public const string Section = "FeatureFlags";

    /// <summary>
    /// Path to the JSON flag file. Defaults to <c>flags.json</c> (at the service content root /
    /// container mount). Absent files are tolerated by returning an empty
    /// flag set so unit tests aren't required to create the file on disk.
    /// </summary>
    public string FilePath { get; set; } = "flags.json";
}
