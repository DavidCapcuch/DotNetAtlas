using System.Diagnostics;
using System.Diagnostics.Metrics;
using EShop.BFF.Infrastructure.Common.Observability;

namespace EShop.BFF.UnitTests.Observability;

/// <summary>
/// Unit coverage for the <see cref="BffMetrics"/> instrumentation seam (bff.md § 2.4): each record/tag
/// method emits the right instrument with the right <c>bff.endpoint</c> tag. The static meter is
/// process-global, but only this class touches it, so a per-test <see cref="MeterListener"/> filtered by
/// meter + instrument name observes exactly its own measurements (mirrors the repo's saga / Inventory
/// metric-emission tests).
/// </summary>
public sealed class BffMetricsTests
{
    [Fact]
    public void RecordCache_WhenHit_IncrementsCacheHitsTaggedByEndpoint()
    {
        // Arrange
        using var capture = new LongCounterCapture("bff.cache.hits");

        // Act
        BffMetrics.RecordCache(BffMetrics.HomePageEndpoint, hit: true);

        // Assert
        using (new AssertionScope())
        {
            capture.Values.Should().ContainSingle().Which.Should().Be(1);
            capture.Tags.Should().ContainSingle().Which.Should().Contain(tag =>
                tag.Key == BffMetrics.EndpointTag && (string?)tag.Value == BffMetrics.HomePageEndpoint);
        }
    }

    [Fact]
    public void RecordCache_WhenMiss_IncrementsCacheMissesTaggedByEndpoint()
    {
        // Arrange
        using var hits = new LongCounterCapture("bff.cache.hits");
        using var misses = new LongCounterCapture("bff.cache.misses");

        // Act
        BffMetrics.RecordCache(BffMetrics.ProductPageEndpoint, hit: false);

        // Assert
        using (new AssertionScope())
        {
            misses.Values.Should().ContainSingle().Which.Should().Be(1);
            misses.Tags.Should().ContainSingle().Which.Should().Contain(tag =>
                tag.Key == BffMetrics.EndpointTag && (string?)tag.Value == BffMetrics.ProductPageEndpoint);
            hits.Values.Should().BeEmpty("a miss must not increment the hits counter");
        }
    }

    [Fact]
    public void RecordPartialResponse_IncrementsPartialResponseTaggedByEndpoint()
    {
        // Arrange
        using var capture = new LongCounterCapture("bff.partial_response");

        // Act
        BffMetrics.RecordPartialResponse(BffMetrics.HomePageEndpoint);

        // Assert
        using (new AssertionScope())
        {
            capture.Values.Should().ContainSingle().Which.Should().Be(1);
            capture.Tags.Should().ContainSingle().Which.Should().Contain(tag =>
                tag.Key == BffMetrics.EndpointTag && (string?)tag.Value == BffMetrics.HomePageEndpoint);
        }
    }

    [Fact]
    public void TagRequest_SetsEndpointCacheHitAndStaleTagsOnTheCurrentSpan()
    {
        // Arrange — a sampled ActivitySource so an Activity.Current exists to enrich.
        using var source = new ActivitySource("test-bff-tagrequest");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "test-bff-tagrequest",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("request");

        // Act
        BffMetrics.TagRequest(BffMetrics.ProductPageEndpoint, cacheHit: true, stale: false);

        // Assert
        using (new AssertionScope())
        {
            activity.Should().NotBeNull();
            activity!.GetTagItem(BffMetrics.EndpointTag).Should().Be(BffMetrics.ProductPageEndpoint);
            activity.GetTagItem(BffMetrics.CacheHitTag).Should().Be(true);
            activity.GetTagItem(BffMetrics.StaleTag).Should().Be(false);
        }
    }

    [Fact]
    public void TagRequest_WhenNoCurrentSpan_DoesNotThrow()
    {
        // No ActivityListener here, so Activity.Current is null — the enrichment must be a safe no-op.
        var act = () => BffMetrics.TagRequest(BffMetrics.HomePageEndpoint, cacheHit: false, stale: true);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Per-test <see cref="MeterListener"/> that records every <c>long</c> measurement on the named
    /// <see cref="BffMetrics"/> instrument plus a pinned copy of its tags (the runtime reuses the tag
    /// span between callbacks, so it must be copied eagerly).
    /// </summary>
    private sealed class LongCounterCapture : IDisposable
    {
        private readonly MeterListener _listener;

        public LongCounterCapture(string instrumentName)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == BffMetrics.MeterName && instrument.Name == instrumentName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
            {
                Values.Add(measurement);
                Tags.Add(tags.ToArray());
            });
            _listener.Start();
        }

        public List<long> Values { get; } = [];

        public List<KeyValuePair<string, object?>[]> Tags { get; } = [];

        public void Dispose() => _listener.Dispose();
    }
}
