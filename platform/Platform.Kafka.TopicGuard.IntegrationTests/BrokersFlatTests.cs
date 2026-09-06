using Confluent.Kafka;
using Platform.Test.Framework.Kafka.Config;

namespace Platform.Kafka.TopicGuard.IntegrationTests;

/// <summary>
/// <c>BrokersFlat</c> feeds Confluent.Kafka's <c>BootstrapServers</c> and MassTransit's
/// <c>Host(...)</c>, and librdkafka parses that value as a comma-separated list — a semicolon is
/// not a separator, so a multi-broker list joined with one becomes a single unresolvable host.
/// Every environment in this repo configures exactly one broker, which is the only reason a wrong
/// separator is invisible; this test supplies two so it is not.
/// </summary>
/// <remarks>
/// Pins <see cref="KafkaOptions"/>, the test-framework copy. The nine production copies (one per
/// bounded context, plus the saga and the BFF) are identical by convention rather than by sharing,
/// so they are corrected but not pinned here.
/// </remarks>
public sealed class BrokersFlatTests(KafkaTopicGuardFixture fixture)
    : IClassFixture<KafkaTopicGuardFixture>
{
    [Fact]
    public void BrokersFlat_WithSeveralBrokers_ProducesAListAKafkaClientConnectsThrough()
    {
        // The first entry is unreachable on purpose. A client can only answer if it parsed the
        // value as a list and fell through to the second — which is what a single-broker fixture
        // could never show, since one broker reaches the cluster under any separator at all.
        var options = new KafkaOptions
        {
            Brokers = ["localhost:1", fixture.Kafka.KafkaOptions.Brokers.Single()],
            SchemaRegistry = fixture.Kafka.KafkaOptions.SchemaRegistry,
            AvroSerializer = fixture.Kafka.KafkaOptions.AvroSerializer
        };

        using var adminClient = new AdminClientBuilder(
                new AdminClientConfig { BootstrapServers = options.BrokersFlat })
            .SetLogHandler((_, _) => { })
            .SetErrorHandler((_, _) => { })
            .Build();

        var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));

        metadata.Brokers.Should().NotBeEmpty(
            "librdkafka splits bootstrap.servers on ',' only, so a list joined with any other "
            + "separator collapses into one unresolvable host and reaches no broker at all");
    }
}
