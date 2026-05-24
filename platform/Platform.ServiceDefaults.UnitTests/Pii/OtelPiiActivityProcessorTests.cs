using System.Diagnostics;
using Platform.ServiceDefaults.Pii;
using Platform.SharedKernel.Pii;

namespace Platform.ServiceDefaults.UnitTests.Pii;

public class OtelPiiActivityProcessorTests : IDisposable
{
    // Instance-scoped source + listener with a unique name per instance — avoids the static-init
    // race where a sibling test's listener could be invoked during another test's ActivitySource
    // construction (the listener's ShouldListenTo would dereference a not-yet-assigned static).
    private readonly ActivitySource _source;
    private readonly ActivityListener _listener;

    public OtelPiiActivityProcessorTests()
    {
        var name = $"Test.OtelPii.{Guid.NewGuid():N}";
        _source = new ActivitySource(name);
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    [Fact]
    public void OnEnd_RedactsTag_WhenValueTypeMarkedPii()
    {
        var processor = new OtelPiiActivityProcessor();
        using var activity = _source.StartActivity("test")!;
        activity.SetTag("pii.field", new PiiMarkedAddress("221B Baker Street"));

        processor.OnEnd(activity);

        activity.GetTagItem("pii.field").Should().Be("***");
    }

    [Fact]
    public void OnEnd_PreservesTag_WhenValueTypeNotMarkedPii()
    {
        var processor = new OtelPiiActivityProcessor();
        using var activity = _source.StartActivity("test")!;
        activity.SetTag("ordinary.field", new NonPiiOrderSummary("order-42", 99.99m));

        processor.OnEnd(activity);

        activity.GetTagItem("ordinary.field").Should().BeOfType<NonPiiOrderSummary>();
    }

    [Fact]
    public void OnEnd_PreservesPrimitiveTag_EvenWhenSourcePropertyIsPiiTagged()
    {
        // The processor cannot detect property-level [Pii] on a primitive value — runtime sees
        // only the boxed string. Call-site masking handles this case (see
        // PaymentTransactionResponseMapper). This test pins that limitation so future readers
        // do not expect more from the processor.
        var processor = new OtelPiiActivityProcessor();
        using var activity = _source.StartActivity("test")!;
        activity.SetTag("payment.method.id", "tok_visa_4242");

        processor.OnEnd(activity);

        activity.GetTagItem("payment.method.id").Should().Be("tok_visa_4242");
    }

    [Fact]
    public void OnEnd_MixedTags_OnlyRedactsPiiOnes()
    {
        var processor = new OtelPiiActivityProcessor();
        using var activity = _source.StartActivity("test")!;
        activity.SetTag("pii.field", new PiiMarkedAddress("221B Baker Street"));
        activity.SetTag("plain.string", "kept");
        activity.SetTag("plain.int", 42);
        activity.SetTag("plain.record", new NonPiiOrderSummary("order-42", 99.99m));

        processor.OnEnd(activity);

        using var _ = new AssertionScope();
        activity.GetTagItem("pii.field").Should().Be("***");
        activity.GetTagItem("plain.string").Should().Be("kept");
        activity.GetTagItem("plain.int").Should().Be(42);
        activity.GetTagItem("plain.record").Should().BeOfType<NonPiiOrderSummary>();
    }

    [Fact]
    public void OnEnd_NullActivity_Throws()
    {
        var processor = new OtelPiiActivityProcessor();

        var act = () => processor.OnEnd(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Pii]
    private sealed record PiiMarkedAddress(string Street);

    private sealed record NonPiiOrderSummary(string OrderId, decimal Total);
}
