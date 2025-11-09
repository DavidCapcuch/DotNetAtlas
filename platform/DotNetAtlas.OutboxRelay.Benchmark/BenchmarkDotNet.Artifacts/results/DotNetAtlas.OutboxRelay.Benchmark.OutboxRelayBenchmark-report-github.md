```

BenchmarkDotNet v0.15.6, Windows 11 (10.0.26200.7019)
AMD Ryzen 7 7700X 4.50GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.100-rc.1.25451.107
  [Host]             : .NET 10.0.0 (10.0.0-rc.1.25451.107, 10.0.25.45207), X64 RyuJIT x86-64-v4
  OutboxMessageRelay : .NET 10.0.0 (10.0.0-rc.1.25451.107, 10.0.25.45207), X64 RyuJIT x86-64-v4

Job=OutboxMessageRelay  Runtime=.NET 10.0  InvocationCount=25  
IterationCount=2  LaunchCount=1  UnrollFactor=1  
WarmupCount=1  

```
| Method                                              | Mean     | Error       | StdDev    | StdErr   | Min      | Median   | Max      | Q1       | Q3       | Skewness | Kurtosis | Ratio | RatioSD | Rank | Gen0     | Completed Work Items | Lock Contentions | Gen1    | Allocated | Alloc Ratio |
|---------------------------------------------------- |---------:|------------:|----------:|---------:|---------:|---------:|---------:|---------:|---------:|---------:|---------:|------:|--------:|-----:|---------:|---------------------:|-----------------:|--------:|----------:|------------:|
| &#39;Publish 1000 Outbox Messages (No Compression)&#39;     | 31.03 ms | 3,435.06 ms |  7.631 ms | 5.396 ms | 25.64 ms | 31.03 ms | 36.43 ms | 28.33 ms | 33.73 ms |      0.0 |   0.2500 |  1.03 |    0.29 |    1 | 160.0000 |               3.9200 |                - | 80.0000 |    2.8 MB |        1.00 |
| &#39;Publish 1000 Outbox Messages (Snappy Compression)&#39; | 33.92 ms | 4,502.49 ms | 10.002 ms | 7.072 ms | 26.84 ms | 33.92 ms | 40.99 ms | 30.38 ms | 37.45 ms |      0.0 |   0.2500 |  1.13 |    0.36 |    1 | 160.0000 |               4.0400 |                - | 40.0000 |    2.8 MB |        1.00 |
| &#39;Publish 1000 Outbox Messages (Zstd Compression)&#39;   | 40.42 ms |   637.54 ms |  1.416 ms | 1.001 ms | 39.42 ms | 40.42 ms | 41.42 ms | 39.92 ms | 40.92 ms |      0.0 |   0.2500 |  1.34 |    0.27 |    1 | 160.0000 |               4.1200 |                - |       - |    2.8 MB |        1.00 |
