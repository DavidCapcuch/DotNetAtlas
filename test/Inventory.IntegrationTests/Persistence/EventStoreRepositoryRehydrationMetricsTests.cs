using System.Diagnostics.Metrics;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Persistence.EventStore;
using Inventory.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.IntegrationTests.Persistence;

/// <summary>
/// Acceptance for the rehydration-observability slice (ADR-0006 § Observability).
/// Drives 100 <see cref="EventStoreRepository.RehydrateAsync"/> calls against a 1000-event
/// stream on Testcontainers Postgres, measuring the
/// <c>inventory.aggregate.rehydration.duration</c> +
/// <c>inventory.aggregate.rehydration.event_count</c> histograms via
/// <see cref="MeterListener"/> and asserting <c>p99 &lt; 1s</c> per the ADR's
/// "snapshot threshold" alert. The alert opens the v2-snapshot work item; until it
/// fires, ES rehydration on per-product streams is fast enough for v1 traffic.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class EventStoreRepositoryRehydrationMetricsTests : BaseIntegrationTest
{
    private const int StreamLength = 1000;
    private const int RehydrationCount = 100;
    private const double P99ThresholdMs = 1000.0;

    private static readonly DateTimeOffset SeedUtc =
        new(2026, 4, 27, 10, 0, 0, TimeSpan.Zero);

    public EventStoreRepositoryRehydrationMetricsTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task RehydrateAsync_OnThousandEventStream_EmitsBothHistogramsTaggedByProductIdAndStaysUnderOneSecondP99()
    {
        var productId = Guid.NewGuid();

        await SeedThousandEventStreamAsync(productId);

        var durations = new List<double>();
        var counts = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name != "Inventory")
            {
                return;
            }

            if (instrument.Name is "inventory.aggregate.rehydration.duration"
                or "inventory.aggregate.rehydration.event_count")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == "inventory.aggregate.rehydration.duration"
                && TryGetProductIdTag(tags, out var taggedId)
                && taggedId == productId)
            {
                durations.Add(measurement);
            }
        });
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == "inventory.aggregate.rehydration.event_count"
                && TryGetProductIdTag(tags, out var taggedId)
                && taggedId == productId)
            {
                counts.Add(measurement);
            }
        });
        listener.Start();

        // Act: 100 rehydrations of the same 1000-event stream.
        using (var actScope = Fixture.CreateScope())
        {
            var repo = actScope.ServiceProvider.GetRequiredService<EventStoreRepository>();

            for (var i = 0; i < RehydrationCount; i++)
            {
                var aggregate = await repo.RehydrateAsync(productId, TestContext.Current.CancellationToken);
                aggregate.Version.Should().Be(StreamLength,
                    "every rehydration must fold the full 1000-event stream");
            }
        }

        // Assert.
        durations.Should().HaveCount(RehydrationCount,
            "exactly one duration measurement per RehydrateAsync call, all tagged with the test's product_id");
        counts.Should().HaveCount(RehydrationCount,
            "exactly one event-count measurement per RehydrateAsync call, all tagged with the test's product_id");

        counts.Should().AllSatisfy(c => c.Should().Be(StreamLength,
            "every measurement must report the actual 1000-event stream length"));

        durations.Should().AllSatisfy(d => d.Should().BeGreaterThan(0,
            "rehydration of 1000 events from real Postgres + JSON deserialization + Fold cannot be 0ms"));

        var p99 = ComputeP99(durations);
        p99.Should().BeLessThan(P99ThresholdMs,
            $"ADR-0006 § Observability: p99 of inventory.aggregate.rehydration.duration must stay below {P99ThresholdMs}ms — " +
            "above this threshold the alert fires and the v2 snapshot work item opens. Observed p99 was {0}ms.", p99);
    }

    private static bool TryGetProductIdTag(ReadOnlySpan<KeyValuePair<string, object?>> tags, out Guid productId)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == "product_id" && tag.Value is Guid id)
            {
                productId = id;
                return true;
            }
        }

        productId = default;
        return false;
    }

    private static double ComputeP99(IReadOnlyList<double> values)
    {
        // Nearest-rank p99: sort ascending, take ceil(0.99 * n)-1 (zero-based).
        var sorted = values.OrderBy(v => v).ToArray();
        var index = (int)Math.Ceiling(0.99 * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private async Task SeedThousandEventStreamAsync(Guid productId)
    {
        // One AppendAsync call producing 1000 events: 1 Initialize + 999 ReceiveStock.
        // The single SaveChangesAsync writes 1000 stock_events rows + the projection
        // upserts in one transaction — significantly faster than 1000 separate appends
        // (each of which would re-rehydrate + dispatch handlers + SaveChanges).
        using var scope = Fixture.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<EventStoreRepository>();

        var result = await repo.AppendAsync(
            productId,
            item =>
            {
                var initResult = item.Initialize(productId, SeedUtc);
                if (initResult.IsFailed)
                {
                    return initResult;
                }

                for (var i = 1; i < StreamLength; i++)
                {
                    var receiveResult = item.ReceiveStock(
                        quantity: 1,
                        source: StockSource.ReceivingDock,
                        receivedByUserId: null,
                        occurredOnUtc: SeedUtc.AddSeconds(i));
                    if (receiveResult.IsFailed)
                    {
                        return receiveResult;
                    }
                }

                return FluentResults.Result.Ok();
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue("seeding the 1000-event stream is a precondition for the p99 measurement");
        result.Value.Version.Should().Be(StreamLength);
    }
}
