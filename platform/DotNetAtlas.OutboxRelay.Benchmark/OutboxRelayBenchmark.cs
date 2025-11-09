using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnostics.dotMemory;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Confluent.Kafka;
using DotNetAtlas.OutboxRelay.Benchmark.Seed;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Xunit;

namespace DotNetAtlas.OutboxRelay.Benchmark;

[MemoryDiagnoser, ThreadingDiagnoser]
// [DotTraceDiagnoser] // only one of either DotTrace or DotMemory can be turned on at a time
[DotMemoryDiagnoser]
[RankColumn, MinColumn, MeanColumn, MedianColumn, MaxColumn, Q1Column, Q3Column]
[StdDevColumn, StdErrorColumn]
[SkewnessColumn, KurtosisColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[KeepBenchmarkFiles]
[StopOnFirstError]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: LaunchCount, warmupCount: WarmupCount, iterationCount: IterationCount,
    invocationCount: InvocationCount,
    id: "OutboxMessageRelay")]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Disposal is in GlobalCleanup")]
public class OutboxRelayBenchmark
{
    private const int LaunchCount = 1;
    private const int WarmupCount = 1;
    private const int IterationCount = 2;
    private const int InvocationCount = 25;

    private const int BatchSize = 1_000;

    private OutboxMessageRelay _outboxMessageRelayNoCompression = null!;
    private OutboxMessageRelay _outboxMessageRelaySnappy = null!;
    private OutboxMessageRelay _outboxMessageRelayZstd = null!;

    private BenchmarkFixture _fixture = null!;

    /// <summary>
    /// Executed once per Benchmark method.
    /// </summary>
    [GlobalSetup]
    public async Task GlobalSetup()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        Log.Information("OutboxMessageRelay Performance Benchmark");

        _fixture = new BenchmarkFixture();
        await ((IAsyncLifetime)_fixture).InitializeAsync();

        _outboxMessageRelayNoCompression = new OutboxMessageRelayBuilder(_fixture.Services)
            .WithOutboxRelayConfig(opts => opts.BatchSize = BatchSize)
            .WithKafkaProducerConfig(opts => opts.CompressionType = CompressionType.None)
            .Build();

        _outboxMessageRelaySnappy = new OutboxMessageRelayBuilder(_fixture.Services)
            .WithOutboxRelayConfig(opts => opts.BatchSize = BatchSize)
            .WithKafkaProducerConfig(opts => opts.CompressionType = CompressionType.Snappy)
            .Build();

        _outboxMessageRelayZstd = new OutboxMessageRelayBuilder(_fixture.Services)
            .WithOutboxRelayConfig(opts => opts.BatchSize = BatchSize)
            .WithKafkaProducerConfig(opts => opts.CompressionType = CompressionType.Zstd)
            .Build();

        // each PublishOutboxMessages call removes BatchSize count of messages, seed them first.
        // after the end of the benchmark, exactly 1 message should remain
        const int neededNumberOfOutboxMessages =
            ((LaunchCount + WarmupCount + IterationCount) * InvocationCount * BatchSize) + BatchSize + 1;
        var seeder = new BenchmarkSeeder(_fixture.Services);
        await seeder.SeedAsync(neededNumberOfOutboxMessages);

        Log.Information(
            "Benchmark Setup Complete - Created 3 OutboxMessageRelay instances with different compression types");
    }

    [Benchmark(Description = "Publish 1000 Outbox Messages (No Compression)", Baseline = true)]
    public async Task Publish1000OutboxMessages_NoCompression()
    {
        var wasSuccessful = await _outboxMessageRelayNoCompression.PublishOutboxMessagesAsync(CancellationToken.None);
        if (!wasSuccessful)
        {
            throw new InvalidOperationException("Outbox failed to publish messages, aborting benchmark");
        }
    }

    [Benchmark(Description = "Publish 1000 Outbox Messages (Snappy Compression)")]
    public async Task Publish1000OutboxMessages_SnappyCompression()
    {
        var wasSuccessful = await _outboxMessageRelaySnappy.PublishOutboxMessagesAsync(CancellationToken.None);
        if (!wasSuccessful)
        {
            throw new InvalidOperationException("Outbox failed to publish messages, aborting benchmark");
        }
    }

    [Benchmark(Description = "Publish 1000 Outbox Messages (Zstd Compression)")]
    public async Task Publish1000OutboxMessages_ZstdCompression()
    {
        var wasSuccessful = await _outboxMessageRelayZstd.PublishOutboxMessagesAsync(CancellationToken.None);
        if (!wasSuccessful)
        {
            throw new InvalidOperationException("Outbox failed to publish messages, aborting benchmark");
        }
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        Log.Information("Benchmark Cleanup Started");

        _outboxMessageRelayNoCompression.Dispose();
        _outboxMessageRelaySnappy.Dispose();
        _outboxMessageRelayZstd.Dispose();

        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<OutboxDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var messagesLeftCount = await dbContext.OutboxMessages.CountAsync();

        Log.Information("Messages left: {MessageCountAsync}", messagesLeftCount);
        if (messagesLeftCount != 1)
        {
            throw new Exception(
                $"MessagesLeftCount wasn't 1 but {messagesLeftCount}, calculations were wrong, please discard the Benchmark and investigate");
        }

        await ((IAsyncDisposable)_fixture).DisposeAsync();

        Log.Information("Benchmark competed");
    }
}
