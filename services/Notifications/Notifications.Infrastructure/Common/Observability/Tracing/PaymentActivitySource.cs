using System.Diagnostics;

namespace Notifications.Infrastructure.Common.Observability.Tracing;

public static class PaymentActivitySource
{
    public const string ActivitySourceName = ApplicationInfo.AppName;
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ApplicationInfo.Version);

    public static Activity? StartActivity(string name, ActivityKind activityKind = ActivityKind.Internal)
        => ActivitySource.StartActivity(name, activityKind);
}
