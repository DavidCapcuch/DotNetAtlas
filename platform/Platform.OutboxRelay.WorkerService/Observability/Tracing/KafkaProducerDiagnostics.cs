using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Platform.ReliableMessaging.Outbox.Core;

namespace Platform.OutboxRelay.WorkerService.Observability.Tracing;

/// <summary>
/// Provides production-essential OpenTelemetry diagnostics for Kafka producer operations.
/// Only includes essential OTEL semantic conventions to minimize performance impact.
/// </summary>
public static class KafkaProducerDiagnostics
{
    /// <summary>
    /// Starts an activity for Kafka message production following OTEL semantic conventions, parented
    /// on the trace context stored with the outbox row, and stamps that activity's own context onto
    /// <paramref name="message"/> so the consumer becomes its child.
    /// </summary>
    /// <remarks>
    /// The caller builds <paramref name="message"/> from the row's headers, which name the
    /// *producing service's* span. Leaving those on the wire would make the consumer a sibling of
    /// this produce span rather than its child: the trace id would still match end to end, so a
    /// trace search looks healthy, but nothing descends from the produce span and the relay hop
    /// cannot be timed. The row keeps its own headers — they are the link back to the originating
    /// request, and this span is parented on them.
    /// </remarks>
    /// <param name="topic">The Kafka topic name.</param>
    /// <param name="message">
    /// The Kafka message being produced. Its <c>Headers</c> are stamped with this span's trace
    /// context; a null <c>Headers</c> is populated.
    /// </param>
    /// <param name="messageHeaders">Deserialized outbox message headers.</param>
    /// <returns>
    /// Activity for the producer operation, or null if activities are not enabled — in which case
    /// <paramref name="message"/> keeps the row's headers, so the consumer still joins the trace one
    /// hop further up rather than losing it.
    /// </returns>
    internal static Activity? StartProduceActivityAndStampTraceContext(
        string topic,
        Message<string?, byte[]> message,
        Dictionary<string, string>? messageHeaders)
    {
        var activityName = string.Intern($"{topic} {OutboxDiagnosticNames.Kafka.Publish}");

        var parentContext = OutboxTraceContext.Propagator.Extract(
            new PropagationContext(default, Baggage.Current), messageHeaders, ExtractHeader);

        var activity = OutboxRelayActivitySource.ActivitySource.CreateActivity(
            activityName, ActivityKind.Producer, parentContext.ActivityContext);

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

        // After Start(), because that is what assigns the span id the consumer will parent on.
        activity.Start();

        message.Headers ??= [];
        OutboxTraceContext.Propagator.Inject(
            new PropagationContext(activity.Context, Baggage.Current), message.Headers, ReplaceHeader);

        return activity;
    }

    /// <summary>
    /// Overwrites rather than appends: the carrier already holds the row's trace headers, and Kafka
    /// permits duplicate keys, so adding would leave two <c>traceparent</c> values on the wire and
    /// let the consumer pick either.
    /// </summary>
    private static void ReplaceHeader(Headers headers, string key, string value)
    {
        headers.Remove(key);
        headers.Add(key, Encoding.UTF8.GetBytes(value));
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
