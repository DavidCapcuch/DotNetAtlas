using System.Reflection;

namespace Invoicing.Infrastructure.Common.Observability;

/// <summary>
/// Application metadata for Invoicing BC instrumentation.
/// </summary>
public static class ApplicationInfo
{
    /// <summary>
    /// Application name used for telemetry identification. Matches the
    /// docker-compose <c>OTEL_SERVICE_NAME=Invoicing</c> override and the
    /// natural BC name; consistent across appsettings, code, and runtime env.
    /// </summary>
    public const string AppName = "Invoicing";

    private static readonly AssemblyName AssemblyName = typeof(ApplicationInfo).Assembly.GetName();
    public static readonly string Version = AssemblyName.Version!.ToString();
}
