using System.Diagnostics;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Platform.Messaging.Abstractions;
using Serilog.Context;

namespace Platform.KafkaFlow.ProducerHeaders;

/// <summary>
/// Consumer-side middleware that resolves the <see cref="MessageHeaderKeys.CorrelationId"/> Kafka
/// header, validates it as UUID v7, and binds the value to <see cref="Activity.Current"/> and the
/// Serilog <see cref="LogContext"/> for the duration of the handler dispatch. Implements the
/// consumer edge of ADR-0008.
/// </summary>
/// <remarks>
/// Place this middleware <em>first</em> in the consumer pipeline (immediately after the
/// deserializer) so that retries, dead-letter produces, inbox deduplication, and the typed handler
/// all execute inside the correlation-id Activity/LogContext scope.
/// </remarks>
public sealed partial class ConsumerCorrelationIdMiddleware : IMessageMiddleware
{
    private readonly ILogger<ConsumerCorrelationIdMiddleware> _logger;

    /// <summary>
    /// Initializes a new <see cref="ConsumerCorrelationIdMiddleware"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostic traces when the inbound header is absent or invalid.</param>
    public ConsumerCorrelationIdMiddleware(ILogger<ConsumerCorrelationIdMiddleware> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Invoke(IMessageContext context, MiddlewareDelegate next)
    {
        var inbound = context.Headers.GetString(MessageHeaderKeys.CorrelationId);
        var correlationId = ResolveCorrelationId(inbound);

        Activity.Current?.SetTag(CorrelationIdKeys.ActivityTagName, correlationId);
        using (LogContext.PushProperty(CorrelationIdKeys.SerilogPropertyName, correlationId))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    private string ResolveCorrelationId(string? inbound)
    {
        if (string.IsNullOrWhiteSpace(inbound))
        {
            return GenerateUuidV7();
        }

        if (!Guid.TryParse(inbound, out var parsed) || !IsUuidV7(parsed))
        {
            LogMalformedCorrelationId(_logger, inbound);
            return GenerateUuidV7();
        }

        return parsed.ToString();
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Inbound Kafka correlation id '{Inbound}' is not a UUID v7; generated replacement.")]
    private static partial void LogMalformedCorrelationId(ILogger logger, string inbound);

    private static string GenerateUuidV7() => Guid.CreateVersion7().ToString();

    private static bool IsUuidV7(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);
        return (bytes[6] >> 4) == 0x7;
    }
}
