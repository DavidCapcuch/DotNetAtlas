using System.Diagnostics;
using System.Diagnostics.Metrics;
using DotNetAtlas.Application.Common.Observability;

namespace DotNetAtlas.Infrastructure.Common.Observability;

public class DotNetAtlasInstrumentation : IDotNetAtlasInstrumentation, IDisposable
{
    public ActivitySource ActivitySource { get; }

    public DotNetAtlasInstrumentation(IMeterFactory meterFactory)
    {
        ActivitySource = new ActivitySource(ApplicationInfo.AppName, ApplicationInfo.Version);

        var appMeter = meterFactory.Create($"{ApplicationInfo.AppName}.application", ApplicationInfo.Version);
    }

    public Activity? StartActivity(string name, ActivityKind activityKind = ActivityKind.Internal)
        => ActivitySource.StartActivity(name, activityKind);

    public void Dispose()
    {
        ActivitySource.Dispose();
    }
}
