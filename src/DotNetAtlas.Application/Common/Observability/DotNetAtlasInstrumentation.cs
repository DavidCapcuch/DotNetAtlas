using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DotNetAtlas.Application.Common.Observability;

public static class DotNetAtlasInstrumentation
{
    public const string MeterName = ApplicationInfo.AppName;
    public const string ActivitySourceName = ApplicationInfo.AppName;
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ApplicationInfo.Version);
    private static readonly Meter Meter = new Meter(MeterName, ApplicationInfo.Version);

    public static Activity? StartActivity(string name, ActivityKind activityKind = ActivityKind.Internal)
        => ActivitySource.StartActivity(name, activityKind);
}
