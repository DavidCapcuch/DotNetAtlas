namespace DotNetAtlas.OutboxRelay.WorkerService.Observability.Metrics;

/// <summary>
/// Constants for OutboxRelay metric tag keys and values.
/// Follows Prometheus naming conventions and OpenTelemetry semantic conventions.
/// </summary>
internal static class OutboxRelayMetricConstants
{
    /// <summary>
    /// Tag keys for metric dimensions.
    /// </summary>
    public static class Tags
    {
        public const string MessageType = "message_type";
        public const string ProcessedCount = "processed_count";
    }

    /// <summary>
    /// Tag values for processed count categories.
    /// </summary>
    public static class ProcessedCountValues
    {
        public const string None = "none";
        public const string Single = "single";
        public const string Tiny = "tiny";
        public const string Small = "small";
        public const string Medium = "medium";
        public const string Large = "large";
        public const string XLarge = "xlarge";
        public const string XxLarge = "xxlarge";
        public const string Huge = "huge";
        public const string Massive = "massive";
        public const string Gigantic = "gigantic";
    }
}
