using System.Diagnostics;

namespace Weather.Application.Common.Observability.Tracing;

public static class DotNetAtlasActivitySource
{
    public const string ActivitySourceName = ApplicationInfo.AppName;
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ApplicationInfo.Version);

    public static Activity? StartActivity(string name, ActivityKind activityKind = ActivityKind.Internal)
        => ActivitySource.StartActivity(name, activityKind);
}
