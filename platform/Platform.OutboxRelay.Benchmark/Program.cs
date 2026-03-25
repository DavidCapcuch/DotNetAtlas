using BenchmarkDotNet.Running;
using Platform.OutboxRelay.Benchmark;

var summary = BenchmarkRunner.Run<OutboxRelayBenchmark>(args: args);

return summary.HasCriticalValidationErrors ? 1 : 0;
