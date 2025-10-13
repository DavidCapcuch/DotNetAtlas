using System.Diagnostics;
using System.Security.Claims;
using Serilog.Context;

namespace DotNetAtlas.Api.Common.Middlewares;

/// <summary>
/// Enriches logging and tracing context for each request.
/// - Adds CorrelationId to response header and log scope (from "X-Correlation-ID" request header if provided, otherwise uses TraceIdentifier).
/// - Adds UserId to OpenTelemetry Activity and log scope when available.
/// </summary>
internal class RequestContextEnrichmentMiddleware
{
    private const string CorrelationIdHeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public RequestContextEnrichmentMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.FindFirst("sub")?.Value
                     ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            Activity.Current?.SetBaggage("user.id", userId);
            Activity.Current?.SetTag("user.id", userId);
        }

        var correlationId = ResolveCorrelationId(context);
        context.Response.Headers[CorrelationIdHeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("UserId", userId))
        {
            await _next.Invoke(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        context.Request.Headers.TryGetValue(
            CorrelationIdHeaderName,
            out var correlationId);

        return correlationId.FirstOrDefault() ?? context.TraceIdentifier;
    }
}
