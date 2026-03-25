using System.Reflection;

namespace Platform.OutboxRelay.WorkerService.Observability;

/// <summary>
/// Application metadata for telemetry instrumentation.
/// </summary>
public static class ApplicationInfo
{
    /// <summary>
    /// Application name used for telemetry identification.
    /// </summary>
    public const string AppName = "OutboxRelay";

    private static readonly AssemblyName AssemblyName = typeof(ApplicationInfo).Assembly.GetName();
    public static readonly string Version = AssemblyName.Version!.ToString();
}
