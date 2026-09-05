using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Observability.HealthChecks;
using SagaOrchestrators.Common.Observability.Metrics;

namespace SagaOrchestrators.UnitTests.Common;

/// <summary>
/// Pins which band the swept stuck-saga count is reported in. Counting the sagas is
/// <see cref="StuckSagaMetricsCollectorTests"/>; this check only reads what that sweep published.
/// </summary>
public class StuckSagaHealthCheckTests
{
    private const int DegradedThreshold = 5;

    [Fact]
    public async Task CheckHealth_ReportsHealthy_BelowTheDegradedThreshold()
    {
        var result = await CheckHealthAsync(stuckPaymentSagaCount: DegradedThreshold - 1);

        result.Status.Should().Be(
            HealthStatus.Healthy,
            "one saga short of the threshold is still inside the tolerated backlog");
    }

    [Fact]
    public async Task CheckHealth_ReportsDegraded_AtTheDegradedThreshold()
    {
        var result = await CheckHealthAsync(stuckPaymentSagaCount: DegradedThreshold);

        result.Status.Should().Be(
            HealthStatus.Degraded,
            "the threshold is inclusive - exactly {0} stuck sagas is already the degraded band",
            DegradedThreshold);
    }

    [Fact]
    public async Task CheckHealth_StaysDegraded_HoweverLargeTheBacklogGets()
    {
        var result = await CheckHealthAsync(stuckPaymentSagaCount: DegradedThreshold * 1000);

        result.Status.Should().Be(
            HealthStatus.Degraded,
            "every replica counts the same rows, so an unhealthy verdict would drop them all from " +
            "rotation at once while none of them is broken - and a restart does not unstick a saga");
    }

    private static Task<HealthCheckResult> CheckHealthAsync(int stuckPaymentSagaCount)
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        using var provider = services.BuildServiceProvider();

        var stuckSagaMetrics = new StuckSagaMetrics(provider.GetRequiredService<IMeterFactory>());
        stuckSagaMetrics.SetStuckPaymentSagaCount(stuckPaymentSagaCount);

        return new StuckSagaHealthCheck(
                stuckSagaMetrics,
                Options.Create(new StuckSagaOptions
                {
                    StuckSagaThresholdMinutes = 30,
                    SweepIntervalSeconds = 60,
                    MaxStuckSagasBeforeDegraded = DegradedThreshold
                }))
            .CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
    }
}
