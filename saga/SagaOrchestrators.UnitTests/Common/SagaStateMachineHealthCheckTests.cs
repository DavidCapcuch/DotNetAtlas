using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Platform.SharedKernel.Base;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Observability.HealthChecks;
using SagaOrchestrators.Common.Persistence.Database;
using SagaOrchestrators.Payments.PaymentProcessingSaga;

namespace SagaOrchestrators.UnitTests.Common;

/// <summary>
/// Pins the stuck-saga sweep: which rows it counts, and which of the three bands it reports.
/// <para>
/// The sweep excludes <see cref="PaymentProcessingSagaState.TerminalStates"/>, but that exclusion
/// is a backstop rather than the live discriminator — every terminal transition calls
/// <c>Finalize()</c> under <c>SetCompletedWhenFinalized()</c>, so the row is deleted instead of
/// coming to rest in a terminal state. What actually decides the count is therefore the age
/// predicate, which is why the boundary cases below carry the weight.
/// </para>
/// </summary>
public class SagaStateMachineHealthCheckTests
{
    private const int ThresholdMinutes = 30;
    private const int DegradedThreshold = 5;
    private const int UnhealthyThreshold = 20;

    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckHealth_IgnoresLongIdleSagas_SittingInATerminalState()
    {
        await using var context = CreateContext(nameof(CheckHealth_IgnoresLongIdleSagas_SittingInATerminalState));
        await SeedAsync(
            context,
            nameof(PaymentProcessingSagaOrchestrator.AuthorizationFailed),
            Now.AddDays(-10),
            count: UnhealthyThreshold * 2);

        var result = await CheckHealthAsync(context);

        result.Status.Should().Be(
            HealthStatus.Healthy,
            "a saga that reached a terminal state is finished, so no amount of idling makes it stuck");
    }

    [Fact]
    public async Task CheckHealth_ReportsUnhealthy_AtTheUnhealthyThreshold()
    {
        await using var context = CreateContext(nameof(CheckHealth_ReportsUnhealthy_AtTheUnhealthyThreshold));
        await SeedIdleNonTerminalAsync(context, count: UnhealthyThreshold);

        var result = await CheckHealthAsync(context);

        result.Status.Should().Be(
            HealthStatus.Unhealthy,
            "the threshold is inclusive - exactly {0} idle sagas is already the unhealthy band",
            UnhealthyThreshold);
    }

    [Fact]
    public async Task CheckHealth_ReportsDegraded_AtTheDegradedThreshold()
    {
        await using var context = CreateContext(nameof(CheckHealth_ReportsDegraded_AtTheDegradedThreshold));
        await SeedIdleNonTerminalAsync(context, count: DegradedThreshold);

        var result = await CheckHealthAsync(context);

        result.Status.Should().Be(
            HealthStatus.Degraded,
            "exactly {0} idle sagas warns without failing readiness, so no restart is provoked",
            DegradedThreshold);
    }

    [Fact]
    public async Task CheckHealth_IgnoresSagas_TouchedExactlyOnTheThreshold()
    {
        await using var context = CreateContext(nameof(CheckHealth_IgnoresSagas_TouchedExactlyOnTheThreshold));
        await SeedAsync(
            context,
            nameof(PaymentProcessingSagaOrchestrator.AwaitingCapture),
            Now.AddMinutes(-ThresholdMinutes),
            count: UnhealthyThreshold * 2);

        var result = await CheckHealthAsync(context);

        result.Status.Should().Be(
            HealthStatus.Healthy,
            "the age comparison is strict, so a saga touched exactly on the threshold has not yet aged out");
    }

    [Fact]
    public async Task CheckHealth_IgnoresSagas_TouchedInsideTheThreshold()
    {
        await using var context = CreateContext(nameof(CheckHealth_IgnoresSagas_TouchedInsideTheThreshold));
        await SeedAsync(
            context,
            nameof(PaymentProcessingSagaOrchestrator.AwaitingCapture),
            Now.AddMinutes(-(ThresholdMinutes - 1)),
            count: UnhealthyThreshold * 2);

        var result = await CheckHealthAsync(context);

        result.Status.Should().Be(
            HealthStatus.Healthy,
            "a saga that moved inside the threshold is progressing, however many of them there are");
    }

    private static Task<HealthCheckResult> CheckHealthAsync(SagaDbContext context) =>
        new SagaStateMachineHealthCheck(
                context,
                NullLogger<SagaStateMachineHealthCheck>.Instance,
                Options.Create(new SagaHealthCheckOptions
                {
                    StuckSagaThresholdMinutes = ThresholdMinutes,
                    MaxStuckSagasBeforeDegraded = DegradedThreshold,
                    MaxStuckSagasBeforeUnhealthy = UnhealthyThreshold
                }),
                new FakeTimeProvider(Now))
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

    private static SagaDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<SagaDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static Task SeedIdleNonTerminalAsync(SagaDbContext context, int count) =>
        SeedAsync(
            context,
            nameof(PaymentProcessingSagaOrchestrator.AwaitingCapture),
            Now.AddMinutes(-(ThresholdMinutes + 1)),
            count);

    private static async Task SeedAsync(
        SagaDbContext context,
        string currentState,
        DateTimeOffset lastModifiedUtc,
        int count)
    {
        for (var i = 0; i < count; i++)
        {
            var entry = context.Add(new PaymentProcessingSagaState
            {
                CorrelationId = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                CurrentState = currentState,
                Currency = "USD",
                IdempotencyKey = $"idempotency-{i}"
            });

            // Mirrors UpdateAuditableEntitiesInterceptor: the audit columns have no public setter,
            // so the EF entry is the only way to age a row. This works only because CreateContext
            // registers no interceptor - adding one would re-stamp these rows to the current time
            // and silently defeat every age assertion above.
            entry.Property(nameof(IAuditableEntity.CreatedUtc)).CurrentValue = lastModifiedUtc;
            entry.Property(nameof(IAuditableEntity.LastModifiedUtc)).CurrentValue = lastModifiedUtc;
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
