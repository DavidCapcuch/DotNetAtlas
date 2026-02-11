using System.Reflection;

namespace Ordering.Application.Common.Observability;

/// <summary>
/// Application metadata for OrderingService instrumentation.
/// </summary>
public static class ApplicationInfo
{
    /// <summary>
    /// Application name used for telemetry identification.
    /// </summary>
    public const string AppName = "OrderingService";

    private static readonly AssemblyName AssemblyName = typeof(ApplicationInfo).Assembly.GetName();
    public static readonly string Version = AssemblyName.Version!.ToString();
}
