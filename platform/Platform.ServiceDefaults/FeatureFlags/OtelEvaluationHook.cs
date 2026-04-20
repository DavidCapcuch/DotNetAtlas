using System.Diagnostics;
using OpenFeature;
using OpenFeature.Model;

namespace Platform.ServiceDefaults.FeatureFlags;

/// <summary>
/// OpenFeature <see cref="Hook"/> that emits an <c>Activity</c> event
/// <c>feature_flag.evaluated</c> on every successful flag evaluation (ADR-0014 line 106).
/// </summary>
/// <remarks>
/// Null-safe when <see cref="Activity.Current"/> is <c>null</c> (background threads, uninstrumented
/// paths). Tag names follow the OpenFeature OTel semantic-convention draft: <c>feature_flag.key</c>,
/// <c>feature_flag.variant</c>, <c>feature_flag.value</c>, <c>feature_flag.targeting_key</c>.
/// </remarks>
public sealed class OtelEvaluationHook : Hook
{
    /// <summary>OTel event name written by this hook.</summary>
    public const string EventName = "feature_flag.evaluated";

    /// <inheritdoc />
    public override ValueTask AfterAsync<T>(
        HookContext<T> context,
        FlagEvaluationDetails<T> details,
        IReadOnlyDictionary<string, object>? hints = null,
        CancellationToken cancellationToken = default)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return ValueTask.CompletedTask;
        }

        var tags = new ActivityTagsCollection
        {
            { "feature_flag.key", details.FlagKey },
            { "feature_flag.variant", details.Variant ?? string.Empty },
            { "feature_flag.value", details.Value?.ToString() ?? string.Empty },
            { "feature_flag.targeting_key", context.EvaluationContext?.TargetingKey ?? string.Empty },
        };

        activity.AddEvent(new ActivityEvent(EventName, tags: tags));
        return ValueTask.CompletedTask;
    }
}
