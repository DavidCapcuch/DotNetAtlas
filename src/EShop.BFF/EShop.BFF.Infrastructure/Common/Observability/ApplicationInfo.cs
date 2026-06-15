using System.Reflection;

namespace EShop.BFF.Infrastructure.Common.Observability;

/// <summary>Static service identity for OpenTelemetry resource attributes.</summary>
internal static class ApplicationInfo
{
    public const string AppName = "BFF";

    public static readonly string Version =
        typeof(ApplicationInfo).Assembly.GetName().Version?.ToString() ?? "unknown";
}
