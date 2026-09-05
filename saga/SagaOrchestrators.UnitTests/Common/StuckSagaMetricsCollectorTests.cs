using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Platform.SharedKernel.Base;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Persistence.Database;
using SagaOrchestrators.Payments.PaymentProcessingSaga;

namespace SagaOrchestrators.UnitTests.Common;

/// <summary>
/// Pins the stuck-saga sweep: which rows it counts, and that the count reaches the gauge that
/// <c>StuckSagaHealthCheck</c> reads.
/// <para>
/// The sweep excludes <see cref="PaymentProcessingSagaState.TerminalStates"/>, but that exclusion
/// is a backstop rather than the live discriminator — every terminal transition calls
/// <c>Finalize()</c> under <c>SetCompletedWhenFinalized()</c>, so the row is deleted instead of
/// coming to rest in a terminal state. What actually decides the count is therefore the age
/// predicate, which is why the boundary cases below carry the weight.
/// </para>
/// </summary>
public class StuckSagaMetricsCollectorTests
{
    private const int ThresholdMinutes = 30;
    private const int SagaCount = 7;

    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_CountsIdleNonTerminalSagas()
    {
        var stuckCount = await SweepAsync(
            nameof(Sweep_CountsIdleNonTerminalSagas),
            nameof(PaymentProcessingSagaOrchestrator.AwaitingCapture),
            Now.AddMinutes(-(ThresholdMinutes + 1)));

        stuckCount.Should().Be(
            SagaCount,
            "a non-terminal saga that has not moved past the threshold is what stuck means");
    }

    [Fact]
    public async Task Sweep_IgnoresLongIdleSagas_SittingInATerminalState()
    {
        var stuckCount = await SweepAsync(
            nameof(Sweep_IgnoresLongIdleSagas_SittingInATerminalState),
            nameof(PaymentProcessingSagaOrchestrator.AuthorizationFailed),
            Now.AddDays(-10));

        stuckCount.Should().Be(
            0,
            "a saga that reached a terminal state is finished, so no amount of idling makes it stuck");
    }

    [Fact]
    public async Task Sweep_IgnoresSagas_TouchedExactlyOnTheThreshold()
    {
        var stuckCount = await SweepAsync(
            nameof(Sweep_IgnoresSagas_TouchedExactlyOnTheThreshold),
            nameof(PaymentProcessingSagaOrchestrator.AwaitingCapture),
            Now.AddMinutes(-ThresholdMinutes));

        stuckCount.Should().Be(
            0,
            "the age comparison is strict, so a saga touched exactly on the threshold has not yet aged out");
    }

    [Fact]
    public async Task Sweep_IgnoresSagas_TouchedInsideTheThreshold()
    {
        var stuckCount = await SweepAsync(
            nameof(Sweep_IgnoresSagas_TouchedInsideTheThreshold),
            nameof(PaymentProcessingSagaOrchestrator.AwaitingCapture),
            Now.AddMinutes(-(ThresholdMinutes - 1)));

        stuckCount.Should().Be(
            0,
            "a saga that moved inside the threshold is progressing, however many of them there are");
    }

    private static async Task<int> SweepAsync(
        string databaseName,
        string currentState,
        DateTimeOffset lastModifiedUtc)
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddDbContext<SagaDbContext>(options => options.UseInMemoryDatabase(databaseName));

        await using var provider = services.BuildServiceProvider();

        using (var seedScope = provider.CreateScope())
        {
            await SeedAsync(
                seedScope.ServiceProvider.GetRequiredService<SagaDbContext>(),
                currentState,
                lastModifiedUtc);
        }

        var stuckSagaMetrics = new StuckSagaMetrics(provider.GetRequiredService<IMeterFactory>());

        var collector = new StuckSagaMetricsCollector(
            stuckSagaMetrics,
            Options.Create(new StuckSagaOptions
            {
                StuckSagaThresholdMinutes = ThresholdMinutes,
                SweepIntervalSeconds = 60,
                MaxStuckSagasBeforeDegraded = 2
            }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(Now),
            NullLogger<StuckSagaMetricsCollector>.Instance);

        await collector.SweepAsync(TestContext.Current.CancellationToken);

        return stuckSagaMetrics.StuckPaymentSagaCount;
    }

    private static async Task SeedAsync(
        SagaDbContext context,
        string currentState,
        DateTimeOffset lastModifiedUtc)
    {
        for (var i = 0; i < SagaCount; i++)
        {
            var entry = context.Add(new PaymentProcessingSagaState
            {
                CorrelationId = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                CurrentState = currentState,
                Currency = "USD",
                IdempotencyKey = $"idempotency-{i}"
            });

            // Mirrors UpdateAuditableEntitiesInterceptor: the audit columns have no public setter,
            // so the EF entry is the only way to age a row. This works only because the provider
            // above registers no interceptor - adding one would re-stamp these rows to the current
            // time and silently defeat every age assertion here.
            entry.Property(nameof(IAuditableEntity.CreatedUtc)).CurrentValue = lastModifiedUtc;
            entry.Property(nameof(IAuditableEntity.LastModifiedUtc)).CurrentValue = lastModifiedUtc;
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
