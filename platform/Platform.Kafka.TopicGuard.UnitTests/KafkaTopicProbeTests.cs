using Confluent.Kafka;
using NSubstitute;

namespace Platform.Kafka.TopicGuard.UnitTests;

public sealed class KafkaTopicProbeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public void FindMissingTopics_WhenEveryRequiredTopicExists_ReturnsNothing()
    {
        var adminClient = AdminClientReporting("catalog.products", "catalog.categories");

        var missing = Probe(adminClient)
            .FindMissingTopics(["catalog.products", "catalog.categories"], Timeout);

        using var _ = new AssertionScope();
        missing.Should().BeEmpty();

        // Each probe builds its own client, so a dropped Dispose leaks a librdkafka handle and its
        // threads on every probe that runs before the check latches.
        adminClient.Received(1).Dispose();
    }

    [Fact]
    public void FindMissingTopics_WhenSeveralAreAbsent_NamesEveryOneOfThem()
    {
        var adminClient = AdminClientReporting("catalog.products");

        var missing = Probe(adminClient).FindMissingTopics(
            ["catalog.products", "catalog.categories", "inventory.stock-events"],
            Timeout);

        missing.Should().BeEquivalentTo(["catalog.categories", "inventory.stock-events"]);
    }

    [Fact]
    public void FindMissingTopics_WhenTheBrokerReportsATopicCarryingAnError_CountsItMissing()
    {
        var adminClient = Substitute.For<IAdminClient>();
        adminClient.GetMetadata(Arg.Any<TimeSpan>()).Returns(new Metadata(
            [new BrokerMetadata(1, "broker", 9092)],
            [new TopicMetadata("catalog.products", [], ErrorCode.UnknownTopicOrPart)],
            originatingBrokerId: 1,
            originatingBrokerName: "broker"));

        var missing = Probe(adminClient).FindMissingTopics(["catalog.products"], Timeout);

        missing.Should().BeEquivalentTo(["catalog.products"]);
    }

    [Fact]
    public void FindMissingTopics_WhenMetadataComesBackWithNoBrokers_ThrowsRatherThanReportingEveryTopicMissing()
    {
        var adminClient = Substitute.For<IAdminClient>();
        adminClient.GetMetadata(Arg.Any<TimeSpan>()).Returns(new Metadata(
            [],
            [],
            originatingBrokerId: -1,
            originatingBrokerName: string.Empty));

        var find = () => Probe(adminClient).FindMissingTopics(["catalog.products"], Timeout);

        find.Should().Throw<KafkaException>(
            "not having reached a broker is a different fact from the topic being absent, and "
            + "reporting it as absence would abort on a connectivity blip");
    }

    /// <summary>
    /// The invariant the whole check rests on. <c>GetMetadata(string, TimeSpan)</c> auto-creates the
    /// topic it asks about on a broker with <c>auto.create.topics.enable=true</c>, so reaching for it
    /// would turn the check into the provisioning mistake it exists to catch — silently, because it
    /// would then always pass.
    /// </summary>
    [Fact]
    public void FindMissingTopics_NeverAsksTheBrokerAboutATopicByName()
    {
        var adminClient = AdminClientReporting("catalog.products");

        Probe(adminClient).FindMissingTopics(["catalog.products", "absent.topic"], Timeout);

        using var _ = new AssertionScope();
        adminClient.Received().GetMetadata(Arg.Any<TimeSpan>());
        adminClient.DidNotReceive().GetMetadata(Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    private static KafkaTopicProbe Probe(IAdminClient adminClient) => new(() => adminClient);

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
