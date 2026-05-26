using System.Reflection;

namespace Basket.Infrastructure.Common.Observability;

/// <summary>
/// Application metadata for Basket BC instrumentation.
/// </summary>
public static class ApplicationInfo
{
    /// <summary>
    /// Application name used for telemetry identification. Matches the
    /// docker-compose <c>OTEL_SERVICE_NAME=Basket</c> override and the natural
    /// BC name; consistent across appsettings, code, and runtime env.
    /// </summary>
    public const string AppName = "Basket";

    private static readonly AssemblyName AssemblyName = typeof(ApplicationInfo).Assembly.GetName();
    public static readonly string Version = AssemblyName.Version!.ToString();
}
