<div align="center">

# ⏰ Background Jobs

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas uses Hangfire for background job processing with SQL Server storage. Jobs include outbox cleanup, inbox cleanup, and scheduled data refresh. The dashboard at `/hangfire-dashboard` provides monitoring and manual job triggering. |

Background jobs handle work that doesn't need to happen during a request: cleanup tasks, scheduled refreshes, and deferred processing. DotNetAtlas uses Hangfire for reliable, persistent job execution.

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    DotNetAtlas API                           │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                  Hangfire Server                        ││
│  │  - Processes jobs from queue                            ││
│  │  - Executes recurring jobs on schedule                  ││
│  │  - Retries failed jobs                                  ││
│  └─────────────────────────────────────────────────────────┘│
└────────────────────────────┬────────────────────────────────┘
                             │
                             │ Poll for jobs
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                      SQL Server                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                  Hangfire Tables                        ││
│  │  - Job (queued jobs)                                    ││
│  │  - State (job state history)                            ││
│  │  - Set (recurring job definitions)                      ││
│  │  - Hash (job parameters)                                ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

## 🔧 Configuration

### Registration

```csharp
services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.FromSeconds(15),
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));

services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 2;
    options.Queues = new[] { "critical", "default", "low" };
});
```

### Dashboard

```csharp
app.MapHangfireDashboard("/hangfire-dashboard", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() },
    DashboardTitle = "DotNetAtlas Jobs"
});
```

## 📦 Job Types

### Fire-and-Forget Jobs

Execute once, as soon as possible:

```csharp
public class EmailService
{
    public void SendWelcomeEmail(Guid userId)
    {
        // Enqueue for immediate execution
        BackgroundJob.Enqueue<IEmailSender>(
            sender => sender.SendWelcomeEmailAsync(userId));
    }
}
```

### Delayed Jobs

Execute once, after a delay:

```csharp
// Send reminder email in 24 hours
BackgroundJob.Schedule<IEmailSender>(
    sender => sender.SendReminderEmailAsync(userId),
    TimeSpan.FromHours(24));
```

### Recurring Jobs

Execute on a schedule:

```csharp
// Clean up old outbox messages daily at 2 AM
RecurringJob.AddOrUpdate<IOutboxCleanupJob>(
    "outbox-cleanup",
    job => job.CleanupAsync(CancellationToken.None),
    "0 2 * * *",  // Cron expression
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.Utc
    });

// Clean up old inbox messages daily at 3 AM
RecurringJob.AddOrUpdate<IInboxCleanupJob>(
    "inbox-cleanup",
    job => job.CleanupAsync(CancellationToken.None),
    "0 3 * * *");

// Refresh weather cache every hour
RecurringJob.AddOrUpdate<IWeatherCacheRefreshJob>(
    "weather-cache-refresh",
    job => job.RefreshAsync(CancellationToken.None),
    "0 * * * *");
```

### Continuations

Execute after another job completes:

```csharp
var jobId = BackgroundJob.Enqueue<IDataImportJob>(
    job => job.ImportAsync(fileId));

BackgroundJob.ContinueJobWith<INotificationJob>(
    jobId,
    job => job.NotifyImportCompleteAsync(fileId));
```

## 🔄 Job Implementations

### Outbox Cleanup Job

```csharp
public class OutboxCleanupJob : IOutboxCleanupJob
{
    private readonly OutboxDbContext _dbContext;
    private readonly ILogger<OutboxCleanupJob> _logger;
    
    public async Task CleanupAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        
        var deleted = await _dbContext.OutboxMessages
            .Where(m => m.ProcessedUtc != null)
            .Where(m => m.ProcessedUtc < cutoff)
            .ExecuteDeleteAsync(ct);
        
        _logger.LogInformation(
            "Deleted {Count} processed outbox messages older than {Cutoff}",
            deleted, cutoff);
    }
}
```

### Weather Cache Refresh Job

```csharp
public class WeatherCacheRefreshJob : IWeatherCacheRefreshJob
{
    private readonly IFusionCache _cache;
    private readonly IWeatherApiClient _weatherApi;
    private readonly ILogger<WeatherCacheRefreshJob> _logger;
    
    public async Task RefreshAsync(CancellationToken ct)
    {
        var popularCities = new[] { "Prague", "London", "New York", "Tokyo" };
        
        foreach (var city in popularCities)
        {
            try
            {
                var forecast = await _weatherApi.GetForecastAsync(city, ct);
                
                await _cache.SetAsync(
                    $"forecast:{city.ToLowerInvariant()}",
                    forecast,
                    options => options.SetDuration(TimeSpan.FromHours(1)),
                    ct);
                
                _logger.LogInformation("Refreshed cache for {City}", city);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh cache for {City}", city);
            }
        }
    }
}
```

## 🔁 Retry Configuration

```csharp
// Global retry policy
GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute
{
    Attempts = 3,
    DelaysInSeconds = new[] { 60, 300, 900 }  // 1m, 5m, 15m
});

// Per-job retry policy
[AutomaticRetry(Attempts = 5, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
public async Task ProcessImportantJobAsync()
{
    // ...
}
```

## 🔭 Observability

Jobs are traced with OpenTelemetry:

```csharp
public class TracedJob
{
    private static readonly ActivitySource ActivitySource = new("DotNetAtlas.Jobs");
    
    public async Task ExecuteAsync()
    {
        using var activity = ActivitySource.StartActivity("Job.Execute");
        activity?.SetTag("job.type", GetType().Name);
        
        // Job logic...
    }
}
```

## 🖥️ Dashboard Features

The Hangfire dashboard at `/hangfire-dashboard` provides:

- **Jobs** - View queued, processing, succeeded, failed jobs
- **Recurring Jobs** - Manage scheduled jobs
- **Retries** - See and retry failed jobs
- **Servers** - Monitor Hangfire server instances
- **Statistics** - Job throughput and performance

## ⚙️ Configuration

```json
{
  "Hangfire": {
    "WorkerCount": 4,
    "Queues": ["critical", "default", "low"],
    "DashboardPath": "/hangfire-dashboard"
  }
}
```

## 📖 Further Reading

- [**Outbox Pattern**](../platform/OutboxPattern.md) - Outbox cleanup job
- [**Inbox Pattern**](../platform/InboxPattern.md) - Inbox cleanup job
- [Hangfire Documentation](https://docs.hangfire.io/)

