using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Kafka.TopicGuard.IntegrationTests;

/// <summary>
/// Drives the check the way the readiness endpoint does — through
/// <see cref="HealthCheckService"/> — against a real broker, which is what the unit tests cannot
/// stand in for: the <c>Metadata</c> shape librdkafka hands back, and how an unreachable broker
/// actually fails.
/// </summary>
public sealed class KafkaTopicsExistenceHealthCheckTests(KafkaTopicGuardFixture fixture)
    : IClassFixture<KafkaTopicGuardFixture>
{
    private const string CheckName = "Kafka topics";
    private const string UnprovisionedTopic = "catalog.categories";

    [Fact]
    public async Task ReadinessReport_WhenEveryRequiredTopicIsProvisioned_IsHealthy()
    {
        var health = ReadinessOver(
            fixture.Kafka.KafkaOptions.BrokersFlat,
            [KafkaTopicGuardFixture.ProvisionedTopic]);

        var report = await health.CheckHealthAsync(Ct);

        using var _ = new AssertionScope();
        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries[CheckName].Description.Should().Contain("verified");
    }

    [Fact]
    public async Task ReadinessReport_WhenARequiredTopicWasNeverProvisioned_IsUnhealthyAndNamesIt()
    {
        var health = ReadinessOver(
            fixture.Kafka.KafkaOptions.BrokersFlat,
            [KafkaTopicGuardFixture.ProvisionedTopic, UnprovisionedTopic]);

        var report = await health.CheckHealthAsync(Ct);

        using var _ = new AssertionScope();
        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries[CheckName].Description.Should()
            .Contain(UnprovisionedTopic, "the operator's next action is to provision that topic")
            .And.NotContain(
                KafkaTopicGuardFixture.ProvisionedTopic,
                "naming a topic that does exist would send them looking in the wrong place");
    }

    /// <summary>
    /// Nothing listens on port 1, so librdkafka retires the metadata request on the probe's own
    /// timeout. The check lets that reach the pipeline rather than reporting an answer it does not
    /// have — an instance that could not ask knows no more than one that found the topics missing.
    /// </summary>
    [Fact]
    public async Task ReadinessReport_WhenTheBrokerCannotBeReached_IsUnhealthyCarryingTheCause()
    {
        var health = ReadinessOver("localhost:1", [KafkaTopicGuardFixture.ProvisionedTopic]);

        var report = await health.CheckHealthAsync(Ct);

        using var _ = new AssertionScope();
        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries[CheckName].Exception.Should().NotBeNull(
            "the cause is what tells an operator the broker is unreachable rather than misprovisioned");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HealthCheckService ReadinessOver(
        string bootstrapServers,
        string[] requiredTopics)
    {
        var services = new ServiceCollection();

        // HealthCheckService takes an ILogger; AddHealthChecks does not bring logging with it.
        services.AddLogging();
        services.AddHealthChecks()
            .AddKafkaTopicsExistenceHealthCheck(bootstrapServers, requiredTopics, CheckName, tags: []);

        return services.BuildServiceProvider().GetRequiredService<HealthCheckService>();
    }
}
