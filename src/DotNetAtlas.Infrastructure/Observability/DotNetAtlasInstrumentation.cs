using System.Diagnostics;
using System.Diagnostics.Metrics;
using DotNetAtlas.Application.Observability;
using DotNetAtlas.Infrastructure.Common;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Infrastructure.Observability
{
    public class DotNetAtlasInstrumentation : IDotNetAtlasInstrumentation
    {
        private readonly ActivitySource _activitySource;

        public DotNetAtlasInstrumentation(IOptions<ApplicationOptions> options, IMeterFactory meterFactory)
        {
            var version = ApplicationInfo.Version;

            var appName = options.Value.AppName;
            _activitySource = new ActivitySource(appName, version);

            var appMeter = meterFactory.Create($"{appName}.application", version);
        }

        public Activity StartActivity(string name, ActivityKind activityKind = ActivityKind.Internal)
            => _activitySource.StartActivity(name, activityKind) ?? new Activity(name).Start();

        public ActivitySource GetActivitySource() => _activitySource;
    }
}