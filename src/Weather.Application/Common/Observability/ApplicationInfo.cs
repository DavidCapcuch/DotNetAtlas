using System.Reflection;

namespace Weather.Application.Common.Observability;

/// <summary>
/// Application metadata for DotNetAtlas instrumentation.
/// </summary>
public static class ApplicationInfo
{
    /// <summary>
    /// Application name used for telemetry identification.
    /// </summary>
    public const string AppName = "DotNetAtlas";

    private static readonly AssemblyName AssemblyName = typeof(ApplicationInfo).Assembly.GetName();
    public static readonly string Version = AssemblyName.Version!.ToString();
}
