using System.Diagnostics;
using System.Reflection;

namespace DotNetAtlas.Infrastructure.Common;

public static class ApplicationInfo
{
    public const string AppName = "DotNetAtlas";
    public static string Version => _version ??= GetCurrentVersion();

    private static string? _version;

    private static string GetCurrentVersion()
    {
        var version = FileVersionInfo
            .GetVersionInfo(Assembly.GetEntryAssembly()!.Location)
            .ProductVersion!
            .Split('+')
            .FirstOrDefault();

        return version ?? "0.0.0";
    }
}
