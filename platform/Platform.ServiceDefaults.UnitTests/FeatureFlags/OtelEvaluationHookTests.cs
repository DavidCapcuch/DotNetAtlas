using System.Diagnostics;
using OpenFeature;
using OpenFeature.Model;
using Platform.ServiceDefaults.FeatureFlags;
using FlagErrorType = OpenFeature.Constant.ErrorType;
using FlagReason = OpenFeature.Constant.Reason;
using FlagValueType = OpenFeature.Constant.FlagValueType;

namespace Platform.ServiceDefaults.UnitTests.FeatureFlags;

public class OtelEvaluationHookTests
{
    private static readonly ActivitySource Source = new(nameof(OtelEvaluationHookTests));

    public OtelEvaluationHookTests()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = s => s.Name == nameof(OtelEvaluationHookTests),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        });
    }

    [Fact]
    public async Task AfterAsync_AddsActivityEventWithExpectedTags()
    {
        // Arrange
        using var activity = Source.StartActivity("test", ActivityKind.Internal)!;
        var hook = new OtelEvaluationHook();
        var context = BuildContext(targetingKey: "buyer-7");
        var details = new FlagEvaluationDetails<bool>(
            flagKey: "catalog.show-discontinued-in-search",
            value: true,
            errorType: FlagErrorType.None,
            reason: FlagReason.TargetingMatch,
            variant: "on");

        // Act
        await hook.AfterAsync(context, details, hints: null, TestContext.Current.CancellationToken);

        // Assert
        using var _ = new AssertionScope();
        activity.Events.Should().ContainSingle(e => e.Name == OtelEvaluationHook.EventName);
        var evt = activity.Events.Single();
        evt.Tags.Should().Contain(new KeyValuePair<string, object?>("feature_flag.key", "catalog.show-discontinued-in-search"));
        evt.Tags.Should().Contain(new KeyValuePair<string, object?>("feature_flag.variant", "on"));
        evt.Tags.Should().Contain(new KeyValuePair<string, object?>("feature_flag.value", "True"));
        evt.Tags.Should().Contain(new KeyValuePair<string, object?>("feature_flag.targeting_key", "buyer-7"));
    }

    [Fact]
    public async Task AfterAsync_WithNoAmbientActivity_IsNoOp()
    {
        // Arrange — no StartActivity on purpose; Activity.Current is null.
        Activity.Current = null;
        var hook = new OtelEvaluationHook();
        var context = BuildContext(targetingKey: null);
        var details = new FlagEvaluationDetails<bool>("k", true, FlagErrorType.None, FlagReason.Default, variant: "on");

        // Act & Assert — no throw.
        var act = async () => await hook.AfterAsync(context, details, hints: null, TestContext.Current.CancellationToken);
        await act.Should().NotThrowAsync();
    }

    private static HookContext<bool> BuildContext(string? targetingKey)
    {
        var evalCtx = targetingKey is null
            ? EvaluationContext.Empty
            : EvaluationContext.Builder().SetTargetingKey(targetingKey).Build();

        return new HookContext<bool>(
            flagKey: "flag",
            defaultValue: false,
            flagValueType: FlagValueType.Boolean,
            clientMetadata: new ClientMetadata("test", "1.0"),
            providerMetadata: new Metadata("in-memory"),
            evaluationContext: evalCtx);
    }
}
