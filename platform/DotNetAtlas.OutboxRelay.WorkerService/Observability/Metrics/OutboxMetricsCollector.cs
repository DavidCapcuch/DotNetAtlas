using DotNetAtlas.Outbox.EntityFrameworkCore.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.OutboxRelay.WorkerService.Observability.Metrics;

public sealed class OutboxMetricsCollector : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxRelayMetrics _outboxRelayMetrics;
    private readonly OutboxMetricsCollectorOptions _outboxMetricsCollectorOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxMetricsCollector> _logger;

    public OutboxMetricsCollector(
        OutboxRelayMetrics outboxRelayMetrics,
        IOptions<OutboxMetricsCollectorOptions> options,
        ILogger<OutboxMetricsCollector> logger,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider)
    {
        _outboxRelayMetrics = outboxRelayMetrics;
        _outboxMetricsCollectorOptions = options.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox metrics monitoring started with update interval: {ReportIntervalSeconds}s",
            _outboxMetricsCollectorOptions.ReportIntervalSeconds);

        using var timer =
            new PeriodicTimer(TimeSpan.FromSeconds(_outboxMetricsCollectorOptions.ReportIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReportOutboxMetrics(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update outbox metrics");
            }
        }

        _logger.LogInformation("Outbox metrics monitoring stopped");
    }

    private async Task ReportOutboxMetrics(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var outboxDbContext = scope.ServiceProvider.GetRequiredService<IOutboxDbContext>();

        var oldestMessageCreated = await outboxDbContext.OutboxMessages
            .OrderBy(m => m.Id)
            .Select(m => m.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var newestMessageCreated = await outboxDbContext.OutboxMessages
            .OrderByDescending(m => m.Id)
            .Select(m => m.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (oldestMessageCreated != default)
        {
            var outboxTailLag = _timeProvider.GetUtcNow() - oldestMessageCreated;
            var tailLagMs = (long)outboxTailLag.TotalMilliseconds;
            _outboxRelayMetrics.SetOutboxTailLagMs(tailLagMs);

            _logger.LogDebug(
                "Updated outbox tail lag metric: {LagMs}ms (oldest message from {CreatedAt})",
                tailLagMs, oldestMessageCreated);
        }
        else
        {
            _outboxRelayMetrics.SetOutboxTailLagMs(0);
            _logger.LogDebug("Outbox is empty, tail lag metric set to 0ms");
        }

        if (newestMessageCreated != default)
        {
            var outboxHeadLag = _timeProvider.GetUtcNow() - newestMessageCreated;
            var headLagMs = (long)outboxHeadLag.TotalMilliseconds;
            _outboxRelayMetrics.SetOutboxHeadLagMs(headLagMs);

            _logger.LogDebug(
                "Updated outbox head lag metric: {LagMs}ms (newest message from {CreatedAt})",
                headLagMs, newestMessageCreated);
        }
        else
        {
            _outboxRelayMetrics.SetOutboxHeadLagMs(0);
            _logger.LogDebug("Outbox is empty, head lag metric set to 0ms");
        }

        var pendingCount = await outboxDbContext.OutboxMessages.CountAsync(cancellationToken);
        _outboxRelayMetrics.SetOutboxPendingMessages(pendingCount);
    }
}
