using System.Diagnostics;
using Confluent.Kafka;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace DotNetAtlas.OutboxRelay.WorkerService.Observability.Tracing;

/// <summary>
/// Provides production-essential OpenTelemetry diagnostics for Kafka producer operations.
/// Only includes essential OTEL semantic conventions to minimize performance impact.
/// </summary>
public static class KafkaProducerDiagnostics
{
    private static readonly TextMapPropagator OtelPropagator = Propagators.DefaultTextMapPropagator;

    /// <summary>
    /// Starts an activity for Kafka message production following OTEL semantic conventions.
    /// Restores trace context from the original outbox message if available.
    /// </summary>
    /// <param name="topic">The Kafka topic name.</param>
    /// <param name="message">The Kafka message being produced.</param>
    /// <param name="messageHeaders">Deserialized outbox message headers.</param>
    /// <returns>Activity for the producer operation, or null if activities are not enabled.</returns>
    internal static Activity? StartProduceActivity(
        string topic,
        Message<string?, byte[]> message,
        Dictionary<string, string>? messageHeaders)
    {
        var activityName = string.Intern($"{topic} {OutboxDiagnosticNames.Kafka.Publish}");

        var parentContext = OtelPropagator.Extract(
            new PropagationContext(default, Baggage.Current),
            messageHeaders,
            ExtractHeader);

        var activity = OutboxRelayActivitySource.ActivitySource.CreateActivity(
            activityName,
            ActivityKind.Producer,
            parentContext.ActivityContext);

        if (activity == null)
        {
            return null;
        }

        // See https://opentelemetry.io/docs/specs/semconv/messaging/kafka/ - production-essential tags only
        activity.SetTag(OutboxDiagnosticNames.Messaging.System, "kafka");
        activity.SetTag(OutboxDiagnosticNames.Messaging.Operation, OutboxDiagnosticNames.Kafka.Publish);
        activity.SetTag(OutboxDiagnosticNames.Messaging.DestinationKind, "topic");
        activity.SetTag(OutboxDiagnosticNames.Messaging.DestinationName, topic);

        if (message.Key != null)
        {
            activity.SetTag(OutboxDiagnosticNames.Kafka.MessageKey, message.Key);
        }

        activity.SetTag(OutboxDiagnosticNames.Messaging.MessageBodySize, message.Value.Length);

        activity.Start();

        return activity;
    }

    private static IEnumerable<string> ExtractHeader(Dictionary<string, string>? headers, string key)
    {
        if (headers != null && headers.TryGetValue(key, out var value))
        {
            return [value];
        }

        return [];
    }
}
