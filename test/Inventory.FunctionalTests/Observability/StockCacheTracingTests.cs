using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Inventory.FunctionalTests.Common;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Inventory.FunctionalTests.Observability;

/// <summary>
/// ADR-0034 + ADR-0016: the read-through stock-availability cache reaches <c>redis-cache</c>
/// through a <em>keyed</em> <see cref="StackExchange.Redis.IConnectionMultiplexer"/>
/// (<c>CacheDependencyInjection</c>). OpenTelemetry's <c>AddRedisInstrumentation()</c> only
/// discovers the <em>unkeyed</em> multiplexer in DI, so the keyed instance must be registered
/// with the TracerProvider explicitly — otherwise the cache's Redis hops never surface as
/// spans. This pins that a bulk read emits StackExchange.Redis spans on the host's
/// TracerProvider (the same pipeline that exports to Jaeger).
/// </summary>
[Collection<FunctionalTestCollection>]
public sealed class StockCacheTracingTests : BaseApiTest
{
    // ActivitySource name of OpenTelemetry.Instrumentation.StackExchangeRedis (its assembly name).
    private const string RedisInstrumentationSource = "OpenTelemetry.Instrumentation.StackExchangeRedis";

    private const string BulkRoute = "/api/v1/inventory/stock-items/bulk";

    public StockCacheTracingTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task BulkRead_EmitsRedisCacheSpans()
    {
        var capturedRedisSpans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RedisInstrumentationSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = capturedRedisSpans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        // A bulk read flows through the read-through cache, whose FusionCache L2 lookup issues a
        // GET over the keyed redis-cache multiplexer — even for an unknown product (miss).
        var response = await Fixture.HttpClientRegistry.NonAuthClient.PostAsJsonAsync(
            BulkRoute,
            new { productIds = new[] { Guid.CreateVersion7() } },
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var tracerProvider = Fixture.Services.GetRequiredService<TracerProvider>();
        var tracedRedisHop = await WaitForRedisSpanAsync(tracerProvider, capturedRedisSpans, TimeSpan.FromSeconds(15));

        tracedRedisHop.Should().BeTrue(
            "the keyed redis-cache multiplexer (ADR-0034) must be registered with the OTel TracerProvider " +
            "so the stock-availability cache's Redis commands surface as spans");
    }

    private static async Task<bool> WaitForRedisSpanAsync(
        TracerProvider tracerProvider,
        ConcurrentQueue<Activity> capturedRedisSpans,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            // The StackExchange.Redis instrumentation drains profiled commands into spans on a
            // background timer (default 10s) or on ForceFlush — flush so the assertion does not
            // race the drain.
            tracerProvider.ForceFlush(2000);
            if (!capturedRedisSpans.IsEmpty)
            {
                return true;
            }

            await Task.Delay(150, TestContext.Current.CancellationToken);
        }

        return false;
    }
}
