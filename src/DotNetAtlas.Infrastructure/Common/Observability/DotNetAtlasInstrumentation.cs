using System.Diagnostics;
using System.Diagnostics.Metrics;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Infrastructure.Common.Config;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Infrastructure.Common.Observability;

public class DotNetAtlasInstrumentation : IDotNetAtlasInstrumentation, IDisposable
{
    private readonly ActivitySource _activitySource;

    public DotNetAtlasInstrumentation(IOptions<ApplicationOptions> options, IMeterFactory meterFactory)
    {
        var version = ApplicationInfo.Version;

        var appName = options.Value.AppName;
        _activitySource = new ActivitySource(appName, version);

        var appMeter = meterFactory.Create($"{appName}.application", version);
    }

    public Activity? StartActivity(string name, ActivityKind activityKind = ActivityKind.Internal)
        => _activitySource.StartActivity(name, activityKind);

    public ActivitySource GetActivitySource() => _activitySource;

    public void Dispose()
    {
        _activitySource.Dispose();
    }
}
