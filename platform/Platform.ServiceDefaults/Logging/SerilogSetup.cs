using Elastic.Serilog.Enrichers.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Platform.ServiceDefaults.Config;
using Platform.ServiceDefaults.Pii;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Exceptions.Core;
using Serilog.Exceptions.EntityFrameworkCore.Destructurers;
using Serilog.Sinks.OpenTelemetry;
using Serilog.Templates;
using Serilog.Templates.Themes;
using SerilogTracing.Expressions;

namespace Platform.ServiceDefaults.Logging;

/// <summary>
/// Configures Serilog logging with environment-specific sinks and enrichers.
/// </summary>
internal static class SerilogSetup
{
    /// <summary>
    /// Configures Serilog logging with sinks and enrichers.
    /// Sets up console, Seq, and OpenTelemetry sinks based on the environment.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="isClusterEnvironment">Whether running in a cluster environment.</param>
    /// <param name="options">Serilog configuration options.</param>
    /// <returns>The configured web application builder.</returns>
    internal static WebApplicationBuilder UseSerilogInternal(
        this WebApplicationBuilder builder,
        bool isClusterEnvironment,
        SerilogOptions options)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            var oltpExporterEndpoint = context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

            var httpAccessor = services.GetRequiredService<IHttpContextAccessor>();

            configuration
                .ReadFrom.Configuration(builder.Configuration)

                // PlatformExceptionHandler already logs every unhandled exception. AddServiceDefaults
                // opts back into ExceptionHandlerMiddleware's diagnostics to keep the error.type
                // metric tag, which also re-enables the middleware's own UnhandledException line —
                // this drops that duplicate while leaving the metric tag and EventSource event
                // untouched. Serilog has no None level; the middleware never logs above Error.
                // Trade-off: this silences the whole category, so the middleware's rarer
                // "exception thrown attempting to execute the error handler" line goes too. That
                // path still surfaces — the middleware rethrows and Kestrel logs the failure.
                .MinimumLevel.Override(
                    "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware",
                    LogEventLevel.Fatal)
                .Destructure.With<PiiDestructuringPolicy>()
                .Enrich.WithEcsHttpContext(httpAccessor)
                .Enrich.FromLogContext()
                .Enrich.WithExceptionDetails(new DestructuringOptionsBuilder()
                    .WithDefaultDestructurers()
                    .WithDestructurers([new DbUpdateExceptionDestructurer()]));

            if (isClusterEnvironment)
            {
                configuration.WriteTo.Console();
            }
            else
            {
                configuration.WriteTo.Console(new ExpressionTemplate(
                        "[{@t:HH:mm:ss} {@l:u3}] " +
                        "[{Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}] " +
                        "{#if IsRootSpan()}\u2514\u2500 {#else if IsSpan()}\u251c {#else if @sp is not null}\u2502 {#end}" +
                        "{@m}" +
                        "{#if IsSpan()} ({Milliseconds(Elapsed()):0.###} ms){#end}" +
                        "\n" +
                        "{@x}",
                        theme: TemplateTheme.Code,
                        nameResolver: new TracingNameResolver()))
                    .WriteTo.Seq(options.SeqUrl);

                if (!string.IsNullOrWhiteSpace(oltpExporterEndpoint))
                {
                    configuration.WriteTo.OpenTelemetry(otlpOptions =>
                    {
                        otlpOptions.Endpoint = oltpExporterEndpoint;
                        otlpOptions.ResourceAttributes = new Dictionary<string, object>
                        {
                            ["service.name"] = options.ServiceName
                        };
                        otlpOptions.IncludedData = IncludedData.SpanIdField | IncludedData.TraceIdField |
                                                   IncludedData.SourceContextAttribute;
                    });
                }
            }

            options.ConfigureLogger?.Invoke(configuration, services);
        });

        return builder;
    }
}
