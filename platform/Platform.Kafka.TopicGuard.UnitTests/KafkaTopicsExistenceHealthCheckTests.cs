using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Platform.Kafka.TopicGuard.UnitTests;

public sealed class KafkaTopicsExistenceHealthCheckTests
{
    private const string RequiredTopic = "catalog.products";

    [Fact]
    public async Task CheckHealthAsync_OnceTheTopicsAreVerified_ReportsHealthyWithoutTouchingTheBroker()
    {
        var adminClient = AdminClientReporting(RequiredTopic);
        var check = CheckOver(adminClient);

        var first = await check.CheckHealthAsync(ContextFor(check), Ct);
        var second = await check.CheckHealthAsync(ContextFor(check), Ct);

        using var _ = new AssertionScope();
        first.Status.Should().Be(HealthStatus.Healthy);
        second.Status.Should().Be(HealthStatus.Healthy);
        adminClient.Received(1).GetMetadata(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task CheckHealthAsync_WhenARequiredTopicIsAbsent_ReportsTheRegistrationsFailureStatusNamingIt()
    {
        var check = CheckOver(AdminClientReporting("some.other.topic"));

        var result = await check.CheckHealthAsync(ContextFor(check), Ct);

        using var _ = new AssertionScope();
        result.Status.Should().Be(HealthStatus.Degraded, "the registration's status is what decides");
        result.Description.Should().Contain(RequiredTopic);
    }

    [Fact]
    public async Task CheckHealthAsync_AfterAnAbsentTopicIsCreated_RecoversWithoutARestart()
    {
        var adminClient = Substitute.For<IAdminClient>();
        adminClient.GetMetadata(Arg.Any<TimeSpan>()).Returns(
            _ => MetadataReporting("some.other.topic"),
            _ => MetadataReporting(RequiredTopic));
        var check = CheckOver(adminClient);

        var beforeTheTopicExisted = await check.CheckHealthAsync(ContextFor(check), Ct);
        var afterItWasCreated = await check.CheckHealthAsync(ContextFor(check), Ct);

        using var _ = new AssertionScope();
        beforeTheTopicExisted.Status.Should().Be(HealthStatus.Degraded);
        afterItWasCreated.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTheBrokerIsUnreachable_LetsTheCauseReachThePipeline()
    {
        var adminClient = Substitute.For<IAdminClient>();
        adminClient.GetMetadata(Arg.Any<TimeSpan>())
            .Returns(_ => throw new KafkaException(ErrorCode.Local_AllBrokersDown));
        var check = CheckOver(adminClient);

        var probe = async () => await check.CheckHealthAsync(ContextFor(check), Ct);

        await probe.Should().ThrowAsync<KafkaException>(
            "the health-check pipeline reports an escaping exception as unhealthy with the cause "
            + "attached, which beats swallowing it into a message of our own");
    }

    /// <summary>
    /// The in-flight gate is reset in a <c>finally</c>, so a probe that throws still reopens it.
    /// Reset it only on the success paths instead and one transient broker failure wedges the check
    /// on "already in progress" for the lifetime of the process — it never retries, even once the
    /// broker is back, because the latch that would have ended the retrying was never set either.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_AfterAProbeThrows_StillAcceptsTheNextProbe()
    {
        var adminClient = Substitute.For<IAdminClient>();
        adminClient.GetMetadata(Arg.Any<TimeSpan>()).Returns(
            _ => throw new KafkaException(ErrorCode.Local_AllBrokersDown),
            _ => MetadataReporting(RequiredTopic));
        var check = CheckOver(adminClient);

        var duringTheOutage = async () => await check.CheckHealthAsync(ContextFor(check), Ct);
        await duringTheOutage.Should().ThrowAsync<KafkaException>();

        var afterTheBrokerReturned = await check.CheckHealthAsync(ContextFor(check), Ct);

        using var _ = new AssertionScope();
        afterTheBrokerReturned.Status.Should().Be(HealthStatus.Healthy);
        adminClient.Received(2).GetMetadata(Arg.Any<TimeSpan>());
    }

    /// <summary>
    /// Readiness probes arrive every 15s across every replica. Without the gate each one would
    /// start its own blocking metadata call while the check is still unverified.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_WhileAProbeIsAlreadyRunning_DoesNotStartASecondOne()
    {
        var probeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adminClient = Substitute.For<IAdminClient>();
        adminClient.GetMetadata(Arg.Any<TimeSpan>()).Returns(_ =>
        {
            probeEntered.TrySetResult();
            releaseProbe.Task.Wait(TimeSpan.FromSeconds(5));
            return MetadataReporting(RequiredTopic);
        });
        var check = CheckOver(adminClient);

        var inFlight = Task.Run(() => check.CheckHealthAsync(ContextFor(check), Ct), Ct);
        await probeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        var whileTheFirstWasStillRunning = await check.CheckHealthAsync(ContextFor(check), Ct);

        releaseProbe.SetResult();
        var firstResult = await inFlight;

        using var _ = new AssertionScope();
        whileTheFirstWasStillRunning.Status.Should().Be(HealthStatus.Degraded);
        firstResult.Status.Should().Be(HealthStatus.Healthy);
        adminClient.Received(1).GetMetadata(Arg.Any<TimeSpan>());
    }

    /// <summary>
    /// The status the check reports is decided by the registration, so it is pinned here rather than
    /// left to the nine per-service tests, which assert registration names only.
    /// </summary>
    [Fact]
    public void AddKafkaTopicsExistenceHealthCheck_RegistersTheCheckAsUnhealthyOnFailure()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks()
            .AddKafkaTopicsExistenceHealthCheck("broker:9092", [RequiredTopic], "Kafka topics", tags: []);

        var registration = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations
            .Single(r => r.Name == "Kafka topics");

        registration.FailureStatus.Should().Be(
            HealthStatus.Unhealthy,
            "an instance that has not verified its topics does not know whether it can do its job");
    }

    /// <summary>
    /// An empty required set would verify trivially and report healthy for ever — a check that
    /// silently checks nothing. Rejected at the boundary, so nothing downstream has to tolerate it.
    /// </summary>
    [Fact]
    public void AddKafkaTopicsExistenceHealthCheck_WithNoRequiredTopics_IsRejectedAtRegistration()
    {
        var register = () => new ServiceCollection()
            .AddHealthChecks()
            .AddKafkaTopicsExistenceHealthCheck("broker:9092", [], "Kafka topics", tags: []);

        register.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static KafkaTopicsExistenceHealthCheck CheckOver(IAdminClient adminClient) =>
        new("broker:9092", [RequiredTopic], new KafkaTopicProbe(() => adminClient));

    /// <summary>
    /// Deliberately registers <see cref="HealthStatus.Degraded"/>, which is NOT the status the
    /// production registration picks. Were the two the same, a check that hard-coded its status
    /// would be indistinguishable from one that read the registration's.
    /// </summary>
    private static HealthCheckContext ContextFor(KafkaTopicsExistenceHealthCheck check) => new()
    {
        Registration = new HealthCheckRegistration(
            "Kafka topics",
            check,
            HealthStatus.Degraded,
            tags: null)
    };

    private static IAdminClient AdminClientReporting(params string[] topics)
    {
        var adminClient = Substitute.For<IAdminClient>();
        adminClient.GetMetadata(Arg.Any<TimeSpan>()).Returns(MetadataReporting(topics));
        return adminClient;
    }

    private static Metadata MetadataReporting(params string[] topics) => new(
        [new BrokerMetadata(1, "broker", 9092)],
        [.. topics.Select(topic => new TopicMetadata(topic, [], ErrorCode.NoError))],
        originatingBrokerId: 1,
        originatingBrokerName: "broker");
}
