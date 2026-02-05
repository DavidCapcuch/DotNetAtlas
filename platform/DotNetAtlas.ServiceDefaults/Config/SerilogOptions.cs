using System.Reflection;
using Serilog;

namespace DotNetAtlas.ServiceDefaults.Config;

/// <summary>
/// Configuration options for Serilog setup.
/// </summary>
public sealed class SerilogOptions
{
    /// <summary>
    /// Service name used for OTLP resource attributes.
    /// Defaults to the entry assembly name.
    /// </summary>
    public string ServiceName { get; set; } = Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";

    /// <summary>
    /// URL for the Seq sink (used in non-cluster environments).
    /// Defaults to "http://localhost:5341".
    /// </summary>
    public string SeqUrl { get; set; } = "http://localhost:5341";

    /// <summary>
    /// Optional callback to further configure the Serilog logger.
    /// Called after default enrichers and sinks are configured.
    /// </summary>
    public Action<LoggerConfiguration, IServiceProvider>? ConfigureLogger { get; set; }
}

