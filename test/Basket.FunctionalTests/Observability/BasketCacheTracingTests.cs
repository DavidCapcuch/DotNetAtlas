using System.Collections.Concurrent;
using System.Diagnostics;
using Basket.Application.Abstractions;
using Basket.FunctionalTests.Common;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Basket.FunctionalTests.Observability;

/// <summary>
/// basket.md § 5.4 + ADR-0016: the basket store reaches <c>redis-basket</c> through a
/// <em>keyed</em> <see cref="StackExchange.Redis.IConnectionMultiplexer"/>
/// (<c>PersistenceDependencyInjection</c>). OpenTelemetry's <c>AddRedisInstrumentation()</c>
/// only discovers the <em>unkeyed</em> multiplexer in DI, so the keyed instance must be
/// registered with the TracerProvider explicitly — otherwise the basket store's Redis hops
/// never surface as spans. This pins that a repository read emits StackExchange.Redis spans on
/// the host's TracerProvider (the same pipeline that exports to Jaeger).
/// </summary>
[Collection<FunctionalTestCollection>]
public sealed class BasketCacheTracingTests : BaseApiTest
{
    // ActivitySource name of OpenTelemetry.Instrumentation.StackExchangeRedis (its assembly name).
    private const string RedisInstrumentationSource = "OpenTelemetry.Instrumentation.StackExchangeRedis";

    public BasketCacheTracingTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task BasketRead_EmitsRedisBasketSpans()
    {
        // Arrange
        var capturedRedisSpans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RedisInstrumentationSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = capturedRedisSpans.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        // Act — a repository read flows through the "basket" FusionCache L2 lookup, which issues a
        // GET over the keyed redis-basket multiplexer — even for an unknown user (miss → Result.Ok(null)).
        var repository = Scope.ServiceProvider.GetRequiredService<IBasketRepository>();
        var result = await repository.GetByUserIdAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();

        var tracerProvider = Fixture.Services.GetRequiredService<TracerProvider>();
        var tracedRedisHop = await WaitForRedisSpanAsync(tracerProvider, capturedRedisSpans, TimeSpan.FromSeconds(15));

        // Assert
        tracedRedisHop.Should().BeTrue(
            "the keyed redis-basket multiplexer (basket.md § 5.4 + ADR-0016) must be registered with the OTel " +
            "TracerProvider so the basket store's Redis commands surface as spans");
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
