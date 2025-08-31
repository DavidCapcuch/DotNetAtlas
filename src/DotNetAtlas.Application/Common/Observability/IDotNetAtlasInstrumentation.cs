using System.Diagnostics;

namespace DotNetAtlas.Application.Common.Observability;

public interface IDotNetAtlasInstrumentation
{
    /// <summary>
    /// Creates an activity source.
    /// </summary>
    ActivitySource GetActivitySource();

    /// <summary>
    /// Creates activity from the activity source.
    /// </summary>
    Activity? StartActivity(string name, ActivityKind activityKind = ActivityKind.Internal);
}
