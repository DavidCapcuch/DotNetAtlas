using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace DotNetAtlas.ReliableMessaging.Outbox.Core;

/// <summary>
/// Extension methods for OutboxMessage header serialization/deserialization.
/// </summary>
public static class OutboxMessageHeaderExtensions
{
    /// <summary>
    /// Maximum allowed size for serialized headers in bytes.
    /// This limit ensures headers fit within database column constraints and Kafka header limits.
    /// </summary>
    public const int MaxHeaderSizeBytes = 8192;

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null // Keep original casing
    };

    private static readonly TextMapPropagator OtelPropagator = Propagators.DefaultTextMapPropagator;

    /// <summary>
    /// Serializes headers dictionary to JSON format for storage.
    /// </summary>
    /// <param name="headers">The headers dictionary to serialize.</param>
    /// <returns>JSON string representation of headers, or null if empty.</returns>
    /// <exception cref="InvalidOperationException">Thrown when serialized headers exceed <see cref="MaxHeaderSizeBytes"/>.</exception>
    /// <example>
    /// Input: [("traceparent", "00-abc..."), ("correlation.id", "123")]<br/>
    /// Output: {"traceparent":"00-abc...","correlation.id":"123"}.
    /// </example>
    public static string? SerializeHeaders(Dictionary<string, string> headers)
    {
        if (headers.Count == 0)
        {
            return null;
        }

        var serialized = JsonSerializer.Serialize(headers, JsonSerializerOptions);

        var byteCount = Encoding.UTF8.GetByteCount(serialized);
        if (byteCount > MaxHeaderSizeBytes)
        {
            throw new InvalidOperationException(
                $"Serialized headers exceed maximum size of {MaxHeaderSizeBytes} bytes. " +
                $"Actual size: {byteCount} bytes. Consider reducing header count or size.");
        }

        return serialized;
    }

    /// <summary>
    /// Deserializes JSON headers string back to dictionary.
    /// </summary>
    /// <param name="message">The outbox message containing headers.</param>
    /// <returns>Dictionary of header key-value pairs, or null if no headers exist.</returns>
    /// <example>
    /// Input: {"traceparent":"00-abc...","correlation.id":"123"}<br/>
    /// Output: Dictionary with keys "traceparent", "correlation.id".
    /// </example>
    public static Dictionary<string, string>? DeserializeHeaders(this OutboxMessage message)
    {
        if (string.IsNullOrEmpty(message.Headers))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(message.Headers, JsonSerializerOptions);
    }

    /// <summary>
    /// Builds headers dictionary from Activity context using OpenTelemetry standard propagator.
    /// Uses W3C trace context format.
    /// </summary>
    /// <param name="activity">The current Activity with tracing context.</param>
    /// <returns>Headers dictionary ready for serialization, or null if no activity.</returns>
    public static Dictionary<string, string>? BuildOtelHeadersFromActivity(Activity? activity)
    {
        if (activity == null)
        {
            return null;
        }

        var headers = new Dictionary<string, string>();
        var propagationContext = new PropagationContext(activity.Context, Baggage.Current);

        OtelPropagator.Inject(propagationContext, headers, InjectTraceContext);

        return headers.Count > 0 ? headers : null;
    }

    /// <summary>
    /// Injects trace context into the headers dictionary using OpenTelemetry standard format.
    /// </summary>
    private static void InjectTraceContext(Dictionary<string, string> headers, string key, string value)
    {
        headers[key] = value;
    }
}
