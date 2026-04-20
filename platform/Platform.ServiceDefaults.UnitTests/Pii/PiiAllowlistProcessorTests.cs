using System.Diagnostics;
using Microsoft.Extensions.Options;
using Platform.ServiceDefaults.Pii;

namespace Platform.ServiceDefaults.UnitTests.Pii;

public class PiiAllowlistProcessorTests
{
    private static readonly ActivitySource Source = new(nameof(PiiAllowlistProcessorTests));

    public PiiAllowlistProcessorTests()
    {
        // Ensure the ActivitySource fires activities under test — default is to sample nothing.
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = s => s.Name == nameof(PiiAllowlistProcessorTests),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        });
    }

    [Fact]
    public void OnEnd_AllowedAttribute_Preserved()
    {
        // Arrange
        using var activity = StartActivity();
        activity!.SetTag("correlation.id", "abc-123");

        var sut = CreateProcessor();

        // Act
        sut.OnEnd(activity);

        // Assert
        activity.GetTagItem("correlation.id").Should().Be("abc-123");
    }

    [Fact]
    public void OnEnd_DisallowedAttribute_Dropped()
    {
        // Arrange
        using var activity = StartActivity();
        activity!.SetTag("buyer.email", "leak@example.com");

        var sut = CreateProcessor();

        // Act
        sut.OnEnd(activity);

        // Assert
        activity.GetTagItem("buyer.email").Should().BeNull();
    }

    [Fact]
    public void OnEnd_PrefixMatchedAttribute_Preserved()
    {
        // Arrange
        using var activity = StartActivity();
        activity!.SetTag("http.request.method", "POST"); // default http. prefix
        activity.SetTag("messaging.kafka.partition", 2); // default messaging. prefix

        var sut = CreateProcessor();

        // Act
        sut.OnEnd(activity);

        // Assert
        using var _ = new AssertionScope();
        activity.GetTagItem("http.request.method").Should().Be("POST");
        activity.GetTagItem("messaging.kafka.partition").Should().Be(2);
    }

    [Fact]
    public void OnEnd_AdditionalAttribute_Preserved()
    {
        // Arrange
        using var activity = StartActivity();
        activity!.SetTag("feature.cohort", "canary");

        var sut = CreateProcessor(new PiiAllowlistOptions { AdditionalAttributes = ["feature.cohort"] });

        // Act
        sut.OnEnd(activity);

        // Assert
        activity.GetTagItem("feature.cohort").Should().Be("canary");
    }

    [Fact]
    public void OnEnd_AdditionalPrefix_Preserved()
    {
        // Arrange
        using var activity = StartActivity();
        activity!.SetTag("tenant.id", "acme");

        var sut = CreateProcessor(new PiiAllowlistOptions { AdditionalPrefixes = ["tenant."] });

        // Act
        sut.OnEnd(activity);

        // Assert
        activity.GetTagItem("tenant.id").Should().Be("acme");
    }

    [Fact]
    public void OnEnd_MixedTags_KeepsAllowedAndDropsDisallowed()
    {
        // Arrange
        using var activity = StartActivity();
        activity!.SetTag("http.method", "GET"); // allowed (exact)
        activity.SetTag("buyer.address.city", "Prague"); // disallowed
        activity.SetTag("order.id", "ord-1"); // allowed (exact)
        activity.SetTag("pii.secret", "x"); // disallowed

        var sut = CreateProcessor();

        // Act
        sut.OnEnd(activity);

        // Assert
        using var _ = new AssertionScope();
        activity.GetTagItem("http.method").Should().Be("GET");
        activity.GetTagItem("order.id").Should().Be("ord-1");
        activity.GetTagItem("buyer.address.city").Should().BeNull();
        activity.GetTagItem("pii.secret").Should().BeNull();
    }

    private static Activity? StartActivity() =>
        Source.StartActivity("test", ActivityKind.Internal);

    private static PiiAllowlistProcessor CreateProcessor(PiiAllowlistOptions? options = null)
    {
        var monitor = new TestOptionsMonitor(options ?? new PiiAllowlistOptions());
        return new PiiAllowlistProcessor(monitor);
    }

    private sealed class TestOptionsMonitor(PiiAllowlistOptions current) : IOptionsMonitor<PiiAllowlistOptions>
    {
        public PiiAllowlistOptions CurrentValue => current;
        public PiiAllowlistOptions Get(string? name) => current;
        public IDisposable? OnChange(Action<PiiAllowlistOptions, string?> listener) => null;
    }
}
