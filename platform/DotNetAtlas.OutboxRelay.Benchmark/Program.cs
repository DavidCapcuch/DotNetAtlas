using BenchmarkDotNet.Running;
using DotNetAtlas.OutboxRelay.Benchmark;

var summary = BenchmarkRunner.Run<OutboxRelayBenchmark>(args: args);

return summary.HasCriticalValidationErrors ? 1 : 0;
