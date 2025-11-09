# OutboxRelay Performance Benchmark
## Overview

This benchmark measures the performance of [OutboxMessageRelay](../DotNetAtlas.OutboxRelay.WorkerService/OutboxRelay/OutboxMessageRelay.cs)
when Publishing pre-seeded Outbox Messages to Kafka using real infrastructure (SQL Server + Kafka). Uses [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) library.

### Quick Start

As with integration tests, real infrastructure is spun up using Docker, make sure it's [installed](https://rancherdesktop.io/) and running. \
Before running the benchmark, it's recommended to turn off background applications etc. to free up resources.

**From platform/DotNetAtlas.OutboxRelay.Benchmark run:**
```bash
# Release configuration is MANDATORY for accurate results
dotnet run --project DotNetAtlas.OutboxRelay.Benchmark.csproj -c Release
```

### Running Subset of Benchmarks

```bash
# Only NoCompression benchmark
dotnet run --project DotNetAtlas.OutboxRelay.Benchmark.csproj -c Release -- --filter *NoCompression

# Only Snappy and Zstd
dotnet run --project DotNetAtlas.OutboxRelay.Benchmark.csproj -c Release -- --filter '*Snappy*|*Zstd*'

# Exclude baseline
dotnet run --project DotNetAtlas.OutboxRelay.Benchmark.csproj -c Release -- --filter '*' --filter-exclude '*NoCompression*'
```

## Performance/Memory profiling with [dotMemory](https://www.jetbrains.com/dotmemory/) or [dotTrace](https://www.jetbrains.com/profiler/)

1. **Enable by attribute in [OutboxRelayBenchmark](OutboxRelayBenchmark.cs):** (only one at a time):
   ```csharp
   [DotMemoryDiagnoser]
   // [DotTraceDiagnoser]
   ```
2. **Run Benchmark**
3. **Analyze Snapshots**:
    - Snapshots are saved to BenchmarkDotNet.Artifacts/snapshots
    - Open with JetBrains dotMemory/dotTrace
    - With dotMemory, look for: Large object allocations, memory leaks, Gen2 survivors
    - With dotTrace, look for: CPU hotspots, lock contentions, async bottlenecks

## How the benchmark works
1. Global setup: spins up SQL Server, Kafka, Schema Registry, preseeds outbox messages
2. Multiple [OutboxRelayWorkers](../DotNetAtlas.OutboxRelay.WorkerService/OutboxRelay/OutboxMessageRelay.cs) are created using [OutboxMessageRelayBuilder](OutboxMessageRelayBuilder.cs) with customized configs,
in this benchmark, each one using different a Kafka producer compression algorithm config, with no compression as a baseline.
3. Each one gets its own Benchmark method for Publishing Outbox Messages which measures its performance.


`Note that network latency and bandwidth are minimal in local scenarios, so size gains from compression
might not be noticable as if this setup was run in cluster with connection to real infrastructure.
Different message size, producer config linger.ms and batchsize will also have an impact on results.`

### Job Parameters

Defined in [OutboxRelayBenchmark](OutboxRelayBenchmark.cs):
```csharp
LaunchCount = 1 // Number of process launches
WarmupCount = 1 // Warmup iterations (excluded from results)
IterationCount = 2 // Measured iterations
InvocationCount = 25 // Calls per iteration
Batchsize = 1000 // Outbox Messages published per iteration
```

## Performance Metrics Explained

### Core Metrics

| Metric | Description | Interpretation |
|--------|-------------|----------------|
| **Mean** | Average execution time across all iterations | Primary metric for typical performance. Lower is better. |
| **Median** | Middle value when measurements sorted | Less affected by outliers than Mean. Compare with Mean to detect skew. |
| **StdDev** | Standard Deviation - variability of measurements | **Lower = more consistent**. High StdDev indicates performance instability. |
| **Allocated** | Total memory allocated (all GC generations) | Lower = less GC pressure. Includes short-lived and long-lived objects. |

### Distribution Metrics

| Metric | Description | Interpretation |
|--------|-------------|----------------|
| **Q1** | 25th percentile | 25% of runs were faster than this |
| **Q3** | 75th percentile | 75% of runs were faster than this |
| **Min** | Fastest run | Best-case performance (often unrealistic) |
| **Max** | Slowest run | Worst-case performance (watch for outliers) |
| **IQR** | Interquartile Range (Q3 - Q1) | Spread of middle 50% of results |

### Statistical Shape

| Metric | Description | What It Means |
|--------|-------------|---------------|
| **Skewness** | Distribution asymmetry | • **~0**: Symmetric (normal)<br>• **Positive**: Right tail (occasional slow runs)<br>• **Negative**: Left tail (occasional fast runs) |
| **Kurtosis** | Tail heaviness | • **~0**: Normal distribution<br>• **Positive**: Heavy tails (more outliers)<br>• **Negative**: Light tails (few outliers) |
| **Outliers** | Statistical anomalies | Values > 1.5×IQR beyond quartiles. Can indicate system interference (antivirus, background tasks). |

### Threading Metrics

| Metric | Description | Good vs. Bad |
|--------|-------------|--------------|
| **Completed Work Items** | Thread pool tasks executed | N/A (informational) |
| **Lock Contentions** | Thread synchronization conflicts | **Lower is better**. High = thread blocking. |
| **Gen0 Collections** | Young generation GC events | Frequent, cheap (< 1ms each) |
| **Gen1 Collections** | Mid-generation GC events | Less frequent (< 10ms each) |
| **Gen2 Collections** | Full GC events | **Should be 0-1**. Rare, expensive (> 100ms). Indicates memory pressure. |
