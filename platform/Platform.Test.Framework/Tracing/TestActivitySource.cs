using System.Diagnostics;

namespace Platform.Test.Framework.Tracing;

/// <summary>
/// Activity source for test-harness traces. Decouples Platform.Test.Framework from any
/// specific BC's ActivitySource so the framework has no cross-tier ProjectReference.
/// </summary>
public static class TestActivitySource
{
    public const string ActivitySourceName = "Platform.Test.Framework";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static Activity? StartActivity(string name, ActivityKind activityKind = ActivityKind.Internal)
        => ActivitySource.StartActivity(name, activityKind);
}
