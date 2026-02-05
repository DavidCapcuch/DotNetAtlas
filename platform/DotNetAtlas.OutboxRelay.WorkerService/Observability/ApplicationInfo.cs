using System.Reflection;

namespace DotNetAtlas.OutboxRelay.WorkerService.Observability;

/// <summary>
/// Application metadata for saga instrumentation.
/// </summary>
public static class ApplicationInfo
{
    /// <summary>
    /// Application name used for telemetry identification.
    /// </summary>
    public const string AppName = "DotNetAtlas.OutboxRealy.WorkerService";

    private static readonly AssemblyName AssemblyName = typeof(ApplicationInfo).Assembly.GetName();
    public static readonly string Version = AssemblyName.Version!.ToString();
}
