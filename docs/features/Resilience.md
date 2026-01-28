<div align="center">

# 🛡️ Resilience

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas uses Microsoft.Extensions.Resilience (Polly v8) for retry, circuit breaker, timeout, and hedging policies. These are configured per HTTP client and protect against transient failures in external services. |

External services fail. Networks are unreliable. Resilience patterns help your application handle these failures gracefully instead of cascading them to users.

## 🎯 Resilience Patterns

| Pattern | Purpose |
|---------|---------|
| **Retry** | Automatically retry failed operations |
| **Circuit Breaker** | Stop calling failing services temporarily |
| **Timeout** | Fail fast when operations take too long |
| **Hedging** | Send parallel requests, use first response |
| **Rate Limiter** | Prevent overwhelming downstream services |

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    HTTP Request                              │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                   Total Timeout                              │
│              Maximum time for entire operation               │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                    Retry Policy                              │
│              Retry on transient failures                     │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                  Circuit Breaker                             │
│              Open circuit on repeated failures               │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                  Attempt Timeout                             │
│              Timeout for single attempt                      │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                   HTTP Handler                               │
│              Actual HTTP call                                │
└─────────────────────────────────────────────────────────────┘
```

## 🔧 Configuration

### Standard Resilience Pipeline

```csharp
services.AddHttpClient<IWeatherApiClient, WeatherApiClient>()
    .AddStandardResilienceHandler(options =>
    {
        // Total timeout for entire operation (including retries)
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
        
        // Retry policy
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromMilliseconds(500);
        options.Retry.UseJitter = true;
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        
        // Circuit breaker
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 10;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        
        // Per-attempt timeout
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
    });
```

### Custom Resilience Pipeline

For more control, build a custom pipeline:

```csharp
services.AddHttpClient<IWeatherApiClient, WeatherApiClient>()
    .AddResilienceHandler("weather-api", builder =>
    {
        // Hedging - send parallel requests
        builder.AddHedging(new HedgingStrategyOptions<HttpResponseMessage>
        {
            MaxHedgedAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(200),
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Result?.StatusCode == HttpStatusCode.ServiceUnavailable)
        });
        
        // Retry with custom logic
        builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is HttpRequestException ||
                args.Outcome.Result?.StatusCode >= HttpStatusCode.InternalServerError)
        });
        
        // Circuit breaker
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            SamplingDuration = TimeSpan.FromSeconds(30),
            FailureRatio = 0.5,
            MinimumThroughput = 10,
            BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is not null ||
                args.Outcome.Result?.StatusCode >= HttpStatusCode.InternalServerError)
        });
        
        // Timeout per attempt
        builder.AddTimeout(TimeSpan.FromSeconds(10));
    });
```

## 📊 Retry Strategies

### Exponential Backoff with Jitter

```csharp
options.Retry.BackoffType = DelayBackoffType.Exponential;
options.Retry.Delay = TimeSpan.FromMilliseconds(500);
options.Retry.UseJitter = true;
// Delays: ~500ms, ~1000ms, ~2000ms (with random jitter)
```

### Linear Backoff

```csharp
options.Retry.BackoffType = DelayBackoffType.Linear;
options.Retry.Delay = TimeSpan.FromSeconds(1);
// Delays: 1s, 2s, 3s
```

### Constant Delay

```csharp
options.Retry.BackoffType = DelayBackoffType.Constant;
options.Retry.Delay = TimeSpan.FromSeconds(1);
// Delays: 1s, 1s, 1s
```

## 🔌 Circuit Breaker States

```
┌─────────┐     Failures exceed     ┌─────────┐
│ CLOSED  │ ──────threshold──────► │  OPEN   │
│ (normal)│                         │ (reject)│
└────┬────┘                         └────┬────┘
     │                                   │
     │                          Break duration
     │                              expires
     │                                   │
     │         ┌───────────┐             │
     └─────────│ HALF-OPEN │◄────────────┘
               │  (probe)  │
               └─────┬─────┘
                     │
          Success: Close    Failure: Open
```

## 🔭 Observability

Resilience events are traced and metered:

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Polly"))
    .WithMetrics(metrics => metrics
        .AddMeter("Polly"));
```

Metrics include:
- `resilience.polly.strategy.attempt.duration` - Duration per attempt
- `resilience.polly.strategy.execution.duration` - Total execution duration
- Circuit breaker state changes

## 🎯 Best Practices

| Practice | Reason |
|----------|--------|
| Use jitter | Prevents thundering herd |
| Set total timeout | Bounds maximum wait time |
| Log circuit breaker events | Know when services are failing |
| Different policies per client | Tune for each service's characteristics |
| Test failure scenarios | Verify resilience works |

## ⚙️ Configuration

```json
{
  "Resilience": {
    "WeatherApi": {
      "TotalTimeoutSeconds": 30,
      "RetryAttempts": 3,
      "RetryDelayMs": 500,
      "CircuitBreakerFailureRatio": 0.5,
      "CircuitBreakerBreakDurationSeconds": 30
    }
  }
}
```

## 📖 Further Reading

- [**Caching**](Caching.md) - Combining resilience with caching
- [**Observability**](Observability.md) - Monitoring resilience metrics
- [Polly Documentation](https://www.pollydocs.org/)

