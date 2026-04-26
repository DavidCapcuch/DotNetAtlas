using Inventory.Application.Common.Data;
using Inventory.Application.StockItems.ReleaseReservation;
using Inventory.Domain.StockItems.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// Polls <c>inventory.reservation_audit</c> every <see cref="PollIntervalSeconds"/>
/// for rows where <c>Status='Active' AND ExpiresAtUtc &lt; now()</c> and dispatches
/// <see cref="ReleaseReservationCommand"/> with <see cref="ReleaseReason.Expiry"/>
/// for each — fulfilling the TTL-auto-release contract from inventory.md § 11.
/// The query is index-served by the partial index
/// <c>ix_reservation_audit_active_expiry</c>.
/// </summary>
/// <remarks>
/// <para>
/// At-least-once: audit rows stay <see cref="ReservationStatus.Active"/> until the
/// command commits, so a worker crash mid-tick is recovered by the next tick. The
/// command-handler path is idempotent on already-<c>Released</c>/<c>Confirmed</c>
/// reservations (Result.Ok with no event), so a duplicate dispatch is benign.
/// </para>
/// <para>
/// The timer is built from the injected <see cref="TimeProvider"/> per ADR-0015,
/// which makes the tick cadence deterministically advance-able under
/// <c>FakeTimeProvider</c> in tests.
/// </para>
/// <para>
/// Tests bypass the <see cref="BackgroundService"/> loop entirely and call
/// <see cref="ProcessExpiredReservationsAsync"/> directly — no async-pump
/// coordination required.
/// </para>
/// </remarks>
internal sealed class ReservationExpiryWorker : BackgroundService
{
    private const int PollIntervalSeconds = 60;
    private const int MaxBatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReservationExpiryWorker> _logger;

    public ReservationExpiryWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ReservationExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Eager startup tick — PeriodicTimer waits for the period to elapse before
        // yielding the first tick, which would add up to PollIntervalSeconds of
        // latency on every cold start / pod restart. Run once immediately so any
        // reservation whose TTL elapsed during downtime is released without
        // waiting another full poll interval.
        await TryRunTickAsync("startup", stoppingToken).ConfigureAwait(false);

        // PeriodicTimer's TimeProvider-aware constructor lets FakeTimeProvider
        // drive ticks deterministically in tests via .Advance(...) (ADR-0015).
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(PollIntervalSeconds),
            _timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!await TryRunTickAsync("scheduled", stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs one expiry tick. Returns <c>false</c> when the host has signaled
    /// shutdown so the caller can exit the polling loop; returns <c>true</c>
    /// otherwise (including after a swallowed transient error).
    /// </summary>
    private async Task<bool> TryRunTickAsync(string tickKind, CancellationToken stoppingToken)
    {
        try
        {
            await ProcessExpiredReservationsAsync(stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Shutdown requested during {TickKind} reservation-expiry tick", tickKind);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {TickKind} reservation-expiry tick", tickKind);
            return true;
        }
    }

    /// <summary>
    /// Single-tick entry point. Reads up to <see cref="MaxBatchSize"/> expired
    /// active reservations and dispatches a <see cref="ReleaseReservationCommand"/>
    /// per row. Exposed as <c>internal</c> so integration tests can drive a
    /// deterministic tick without the <see cref="BackgroundService"/> loop.
    /// </summary>
    internal async Task ProcessExpiredReservationsAsync(CancellationToken ct)
    {
        var nowUtc = _timeProvider.GetUtcNow();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IInventoryDbContext>();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReleaseReservationCommand>>();

        var expired = await dbContext.ReservationAudit
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAtUtc < nowUtc)
            .OrderBy(r => r.ExpiresAtUtc)
            .Take(MaxBatchSize)
            .Select(r => new ExpiredReservation(r.ReservationId, r.ProductId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (expired.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Reservation-expiry tick at {NowUtc:O}: {ExpiredCount} expired reservation(s) to release",
            nowUtc, expired.Count);

        foreach (var row in expired)
        {
            try
            {
                var command = new ReleaseReservationCommand
                {
                    ReservationId = row.ReservationId,
                    ProductId = row.ProductId,
                    Reason = ReleaseReason.Expiry,
                    OccurredOnUtc = nowUtc,
                    CorrelationId = null,
                };

                var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                if (result.IsFailed)
                {
                    _logger.LogWarning(
                        "ReleaseReservationCommand(reason=Expiry) failed for reservation {ReservationId} on product {ProductId}: {Errors}",
                        row.ReservationId,
                        row.ProductId,
                        string.Join("; ", result.Errors.Select(e => e.Message)));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unhandled exception releasing expired reservation {ReservationId} on product {ProductId}",
                    row.ReservationId,
                    row.ProductId);
            }
        }
    }

    private readonly record struct ExpiredReservation(Guid ReservationId, Guid ProductId);
}
