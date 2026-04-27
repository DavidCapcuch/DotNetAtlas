using FluentResults;
using Inventory.Application.StockItems.ReleaseReservation;
using Inventory.Domain.StockItems.Errors;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.BackgroundJobs;
using Inventory.Infrastructure.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.CQRS;

namespace Inventory.UnitTests.BackgroundJobs;

/// <summary>
/// M6 follow-up (carried forward to M8 per <c>inventory.md:346,438</c>): unit-level
/// coverage for the warning-log branch of
/// <c>ReservationExpiryWorker.ProcessExpiredReservationsAsync</c> at
/// <c>services/Inventory/Inventory.Infrastructure/BackgroundJobs/ReservationExpiryWorker.cs:152-159</c>.
/// </summary>
/// <remarks>
/// <para>
/// The branch fires on a between-tick race: the worker scans
/// <c>reservation_audit WHERE Status='Active' AND ExpiresAtUtc &lt; now()</c> and reads
/// a row as Active, then between the read and the dispatch the saga's
/// <c>ConfirmReservation</c> (or a competing <c>Release</c>) lands and flips status.
/// The aggregate's <c>StockItem.ReleaseReservation</c> returns
/// <c>Result.Fail(ReservationNotActive(...))</c>; the worker logs a single Warning
/// and continues to the next row without throwing.
/// </para>
/// <para>
/// The existing M6 integration test <c>ConfirmedReservation_NotReleasedAfterExpiry</c>
/// at <c>test/Inventory.IntegrationTests/BackgroundJobs/ReservationExpiryWorkerTests.cs:220</c>
/// covers the case where the confirm landed BEFORE the scan (so the
/// <c>WHERE Status='Active'</c> filter excludes the row at scan time) — it never
/// invokes the warning branch. This unit test fills that gap by stubbing
/// <see cref="ICommandHandler{TCommand}"/> to deterministically fail.
/// </para>
/// </remarks>
public sealed class ReservationExpiryWorkerWarningLogTests
{
    private static readonly DateTimeOffset SeedUtc =
        new(2026, 4, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WhenHandlerReturnsReservationNotActiveError_LogsWarning_AndDoesNotThrow()
    {
        var productId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Stub handler returns Fail(ReservationNotActiveError) — simulates the
        // between-tick race where Confirm landed after the worker's scan but
        // before its dispatch.
        var stubHandler = Substitute.For<ICommandHandler<ReleaseReservationCommand>>();
        stubHandler
            .HandleAsync(Arg.Any<ReleaseReservationCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail(InventoryErrors.ReservationNotActive(productId, reservationId, ReservationStatus.Confirmed)));

        // Hand-rolled capturing logger because the ReservationExpiryWorker is internal and
        // NSubstitute's dynamic-proxy generator (Castle DynamicProxyGenAssembly2) cannot
        // synthesize a proxy for ILogger<InternalType> without a strong-named-aware
        // [InternalsVisibleTo] on Inventory.Infrastructure — a production-code change we
        // do not want for test plumbing. The capturing logger is ~10 lines and reusable.
        var capturedLogger = new CapturingLogger<ReservationExpiryWorker>();

        await using var serviceProvider = BuildServiceProvider(stubHandler);

        await SeedExpiredActiveReservationAsync(serviceProvider, productId, reservationId, orderId);

        // Advance fake time 16 minutes past the seed → the row is past its 15m TTL.
        var fakeTime = new FakeTimeProvider(SeedUtc.AddMinutes(16));

        var worker = new ReservationExpiryWorker(
            scopeFactory: serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            timeProvider: fakeTime,
            logger: capturedLogger);

        // Act + assert: must not throw.
        await worker.ProcessExpiredReservationsAsync(TestContext.Current.CancellationToken);

        // Assert: handler was invoked exactly once with the row's ids.
        await stubHandler.Received(1).HandleAsync(
            Arg.Is<ReleaseReservationCommand>(c =>
                c.ReservationId == reservationId
                && c.ProductId == productId
                && c.Reason == ReleaseReason.Expiry),
            Arg.Any<CancellationToken>());

        // Assert: exactly one Warning entry with the failure-message contract.
        var warnings = capturedLogger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        warnings.Should().ContainSingle(
            "the warning-log branch must fire exactly once for the single race-failed row");
        warnings[0].Message.Should().Contain("ReleaseReservationCommand(reason=Expiry) failed");
        warnings[0].Message.Should().Contain(reservationId.ToString());
        warnings[0].Message.Should().Contain(productId.ToString());

        // Assert: no Error / Critical log fired (the failure is recoverable, not an exception).
        capturedLogger.Entries
            .Where(e => e.Level >= LogLevel.Error)
            .Should().BeEmpty(
                "ReservationNotActive on a stale audit row is recoverable — must NOT escalate to Error/Critical");
    }

    private static ServiceProvider BuildServiceProvider(ICommandHandler<ReleaseReservationCommand> stubHandler)
    {
        var services = new ServiceCollection();

        // Unique InMemory DB name per test run so concurrent tests don't see
        // each other's audit rows.
        var dbName = $"inventory-expiry-warning-{Guid.NewGuid():N}";
        services.AddDbContext<InventoryDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped<Inventory.Application.Common.Data.IInventoryDbContext>(
            sp => sp.GetRequiredService<InventoryDbContext>());

        // The worker resolves ICommandHandler<ReleaseReservationCommand> per-scope.
        // A scoped registration of the singleton stub preserves the substitute's
        // call-recording across all scopes the worker creates.
        services.AddScoped(_ => stubHandler);

        return services.BuildServiceProvider();
    }

    private static async Task SeedExpiredActiveReservationAsync(
        IServiceProvider services, Guid productId, Guid reservationId, Guid orderId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        db.ReservationAudit.Add(new Inventory.Application.Common.ReadModels.ReservationAuditRow
        {
            ReservationId = reservationId,
            ProductId = productId,
            OrderId = orderId,
            Quantity = 2,
            Status = ReservationStatus.Active,
            ReservedAtUtc = SeedUtc,
            ExpiresAtUtc = SeedUtc.AddMinutes(15),
            ResolvedAtUtc = null,
            ReleaseReason = null,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
