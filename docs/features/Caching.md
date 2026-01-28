<div align="center">

# 💾 Caching

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas uses FusionCache for multi-level caching: L1 (in-memory) + L2 (Redis). Features include fail-safe (serve stale on failure), eager refresh, and adaptive timeouts. Weather forecasts are cached to reduce external API calls. |

Caching is essential for performance and resilience. DotNetAtlas uses [FusionCache](https://github.com/ZiggyCreatures/FusionCache), a modern .NET caching library that provides multi-level caching with advanced features.

## 🎯 Why FusionCache?

| Feature | Benefit |
|---------|---------|
| **Multi-level** | L1 (memory) + L2 (Redis) for speed and distribution |
| **Fail-safe** | Serve stale data when backend fails |
| **Eager refresh** | Refresh cache before expiration |
| **Adaptive timeouts** | Adjust timeouts based on conditions |
| **Backplane** | Sync L1 caches across instances |

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Application                             │
│                    cache.GetOrSet()                          │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                    L1: Memory Cache                          │
│              Fast, per-instance, limited size                │
│                     Hit? → Return                            │
└────────────────────────────┬────────────────────────────────┘
                             │ Miss
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                    L2: Redis Cache                           │
│            Shared across instances, larger capacity          │
│                     Hit? → Return + populate L1              │
└────────────────────────────┬────────────────────────────────┘
                             │ Miss
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                    Factory (Data Source)                     │
│              Database, external API, computation             │
│                     Return + populate L1 + L2                │
└─────────────────────────────────────────────────────────────┘
```

## 🔧 Configuration

### Registration

```csharp
services.AddFusionCache()
    .WithDefaultEntryOptions(options =>
    {
        options.Duration = TimeSpan.FromMinutes(5);
        options.FailSafeMaxDuration = TimeSpan.FromHours(1);
        options.FailSafeThrottleDuration = TimeSpan.FromSeconds(30);
        options.FactorySoftTimeout = TimeSpan.FromMilliseconds(500);
        options.FactoryHardTimeout = TimeSpan.FromSeconds(2);
        options.EagerRefreshThreshold = 0.8f;
    })
    .WithSerializer(new FusionCacheSystemTextJsonSerializer())
    .WithDistributedCache(
        services.BuildServiceProvider().GetRequiredService<IDistributedCache>())
    .WithBackplane(
        services.BuildServiceProvider().GetRequiredService<IFusionCacheBackplane>());
```

### Redis Setup

```csharp
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "DotNetAtlas:";
});

services.AddFusionCacheStackExchangeRedisBackplane(options =>
{
    options.Configuration = "localhost:6379";
});
```

## 📦 Usage Examples

### Basic Caching

```csharp
public class WeatherForecastService
{
    private readonly IFusionCache _cache;
    private readonly IWeatherApiClient _weatherApi;
    
    public async Task<WeatherForecast> GetForecastAsync(string city, CancellationToken ct)
    {
        var cacheKey = $"forecast:{city.ToLowerInvariant()}";
        
        return await _cache.GetOrSetAsync(
            cacheKey,
            async token => await _weatherApi.GetForecastAsync(city, token),
            options => options
                .SetDuration(TimeSpan.FromMinutes(15))
                .SetFailSafeMaxDuration(TimeSpan.FromHours(2)),
            ct);
    }
}
```

### With Fail-Safe

When the weather API is down, serve the last known forecast:

```csharp
var forecast = await _cache.GetOrSetAsync(
    cacheKey,
    async token =>
    {
        // This might throw if API is down
        return await _weatherApi.GetForecastAsync(city, token);
    },
    options => options
        .SetDuration(TimeSpan.FromMinutes(15))
        .SetFailSafeMaxDuration(TimeSpan.FromHours(24))  // Serve stale up to 24h
        .SetFailSafeThrottleDuration(TimeSpan.FromMinutes(1)),  // Retry every 1m
    ct);
```

### With Eager Refresh

Refresh the cache before it expires to avoid cache misses:

```csharp
var forecast = await _cache.GetOrSetAsync(
    cacheKey,
    async token => await _weatherApi.GetForecastAsync(city, token),
    options => options
        .SetDuration(TimeSpan.FromMinutes(15))
        .SetEagerRefreshThreshold(0.8f),  // Refresh at 80% of duration (12 min)
    ct);
```

### With Adaptive Timeouts

Adjust timeouts based on current conditions:

```csharp
var forecast = await _cache.GetOrSetAsync(
    cacheKey,
    async (ctx, token) =>
    {
        // ctx.Options can be modified based on conditions
        if (IsHighTraffic())
        {
            ctx.Options.FactorySoftTimeout = TimeSpan.FromMilliseconds(200);
        }
        
        return await _weatherApi.GetForecastAsync(city, token);
    },
    options => options
        .SetDuration(TimeSpan.FromMinutes(15))
        .SetFactorySoftTimeout(TimeSpan.FromMilliseconds(500))
        .SetFactoryHardTimeout(TimeSpan.FromSeconds(2)),
    ct);
```

## 🔭 Observability

FusionCache integrates with OpenTelemetry:

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddFusionCacheInstrumentation());
```

Traces show:
- Cache hits/misses
- L1 vs L2 hits
- Factory execution time
- Fail-safe activations

## 🎯 Cache Invalidation

### Manual Invalidation

```csharp
// Remove specific key
await _cache.RemoveAsync($"forecast:{city}");

// Remove with pattern (requires custom implementation)
await _cache.RemoveByTagAsync("forecasts");
```

### Event-Driven Invalidation

When data changes, invalidate related cache entries:

```csharp
public class ForecastUpdatedEventHandler
{
    public async Task HandleAsync(ForecastUpdatedEvent @event)
    {
        await _cache.RemoveAsync($"forecast:{@event.City}");
    }
}
```

## ⚙️ Configuration Options

```json
{
  "Caching": {
    "DefaultDurationMinutes": 5,
    "FailSafeMaxDurationHours": 1,
    "FactorySoftTimeoutMs": 500,
    "FactoryHardTimeoutMs": 2000,
    "EagerRefreshThreshold": 0.8
  },
  "Redis": {
    "ConnectionString": "localhost:6379",
    "InstanceName": "DotNetAtlas:"
  }
}
```

## 📖 Further Reading

- [**Resilience**](Resilience.md) - Combining caching with resilience patterns
- [**Observability**](Observability.md) - Monitoring cache performance
- [FusionCache Documentation](https://github.com/ZiggyCreatures/FusionCache)

