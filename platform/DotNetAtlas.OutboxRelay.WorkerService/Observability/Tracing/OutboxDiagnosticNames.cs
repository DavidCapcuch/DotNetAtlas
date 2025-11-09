namespace DotNetAtlas.OutboxRelay.WorkerService.Observability.Tracing;

/// <summary>
/// Diagnostic names and constants for OutboxRelay service observability.
/// Follows OpenTelemetry semantic conventions.
/// </summary>
public static class OutboxDiagnosticNames
{
    /// <summary>
    /// Messaging system attributes following OTEL semantic conventions.
    /// </summary>
    public static class Messaging
    {
        /// <summary>
        /// The messaging system being used (e.g., "kafka").
        /// </summary>
        public const string System = "messaging.system";

        /// <summary>
        /// The messaging operation being performed (e.g., "publish", "process").
        /// </summary>
        public const string Operation = "messaging.operation";

        /// <summary>
        /// The kind of message destination (e.g., "topic", "queue").
        /// </summary>
        public const string DestinationKind = "messaging.destination.kind";

        /// <summary>
        /// The name of the message destination (e.g., topic name).
        /// </summary>
        public const string DestinationName = "messaging.destination.name";

        /// <summary>
        /// The size of the message body in bytes.
        /// </summary>
        public const string MessageBodySize = "messaging.message.body.size";
    }

    /// <summary>
    /// Kafka-specific attributes following OTEL semantic conventions.
    /// </summary>
    public static class Kafka
    {
        public const string Publish = "publish";
        public const string MessageKey = "messaging.kafka.message.key";
        public const string MessageOffset = "messaging.kafka.message.offset";
    }

    public static class ErrorTag
    {
        public const string Type = "error.type";
    }
}
