using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Platform.ServiceDefaults.CorrelationId;

/// <summary>
/// Reads (and when necessary generates) the <c>X-Correlation-Id</c> header on inbound HTTP requests,
/// publishes the resolved value onto <see cref="HttpContext.Items"/>, the ambient OpenTelemetry
/// <see cref="Activity"/>, and the Serilog <see cref="LogContext"/>, and echoes the value back on the response.
/// Implements ADR-0008 at the HTTP edge.
/// </summary>
public sealed partial class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly CorrelationIdOptions _options;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    /// <summary>
    /// Initializes a new <see cref="CorrelationIdMiddleware"/>.
    /// </summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="options">Bound correlation-id options.</param>
    /// <param name="logger">Logger for edge diagnostics.</param>
    public CorrelationIdMiddleware(
        RequestDelegate next,
        IOptions<CorrelationIdOptions> options,
        ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Middleware entry point.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var inbound = context.Request.Headers[_options.HeaderName].FirstOrDefault();
        var correlationId = ResolveCorrelationId(inbound);

        if (string.IsNullOrEmpty(correlationId))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Items[CorrelationIdContextKeys.HttpContextItemKey] = correlationId;
        Activity.Current?.SetTag(CorrelationIdContextKeys.ActivityTagName, correlationId);

        // Set the echo header eagerly so clients always receive it — even when downstream
        // middleware short-circuits or an exception handler rewrites the response. Custom exception
        // handlers that rebuild the response must re-apply the header themselves.
        context.Response.Headers[_options.HeaderName] = correlationId;

        using (LogContext.PushProperty(CorrelationIdContextKeys.SerilogPropertyName, correlationId))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    private string ResolveCorrelationId(string? inbound)
    {
        if (string.IsNullOrWhiteSpace(inbound))
        {
            return _options.GenerateWhenMissing ? GenerateUuidV7() : string.Empty;
        }

        if (!Guid.TryParse(inbound, out var parsed) || !IsUuidV7(parsed))
        {
            LogMalformedCorrelationId(_logger, inbound);
            return _options.GenerateWhenMissing ? GenerateUuidV7() : string.Empty;
        }

        return parsed.ToString();
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Inbound correlation id '{Inbound}' is not a UUID v7; generated replacement.")]
    private static partial void LogMalformedCorrelationId(ILogger logger, string inbound);

    private static string GenerateUuidV7() => Guid.CreateVersion7().ToString();

    private static bool IsUuidV7(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);
        return (bytes[6] >> 4) == 0x7;
    }
}
