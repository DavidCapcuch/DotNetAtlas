using Platform.Test.Framework.Kafka;

namespace Platform.Kafka.TopicGuard.IntegrationTests;

/// <summary>
/// One broker for the whole class, with one topic provisioned. Held in a fixture rather than on the
/// test class: xUnit builds a fresh class instance per test, so an instance field would boot a
/// Kafka and a Schema Registry per test case.
/// </summary>
public sealed class KafkaTopicGuardFixture : IAsyncLifetime
{
    public const string ProvisionedTopic = "catalog.products";

    public KafkaTestContainer Kafka { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Kafka.StartAsync(TestContext.Current.CancellationToken);
        await Kafka.CreateKafkaTopicsAsync([ProvisionedTopic]);
    }

    public async ValueTask DisposeAsync() => await Kafka.DisposeAsync();
}
