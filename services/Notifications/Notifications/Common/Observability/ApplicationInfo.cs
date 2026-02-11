using System.Reflection;

namespace Notifications.Common.Observability;

/// <summary>
/// Application metadata for PaymentService instrumentation.
/// </summary>
public static class ApplicationInfo
{
    /// <summary>
    /// Application name used for telemetry identification.
    /// </summary>
    public const string AppName = "PaymentService";

    private static readonly AssemblyName AssemblyName = typeof(ApplicationInfo).Assembly.GetName();
    public static readonly string Version = AssemblyName.Version!.ToString();
}
