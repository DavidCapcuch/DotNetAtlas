using DotNetAtlas.Infrastructure.Communication.Kafka.Config;
using Weather.Contracts;

namespace DotNetAtlas.Test.Shared.Kafka;

/// <summary>
/// Centralized registry for managing Kafka test consumers in integration tests.
/// </summary>
public sealed class KafkaTestConsumerRegistry : IDisposable
{
    private readonly List<IKafkaTestConsumer> _kafkaTestConsumers = [];

    public IReadOnlyList<IKafkaTestConsumer> KafkaTestConsumers => _kafkaTestConsumers.AsReadOnly();

    public KafkaTestConsumer<ForecastRequestedEvent> ForecastRequestedConsumer { get; }

    public KafkaTestConsumerRegistry(KafkaOptions kafkaOptions, TopicsOptions topicsOptions)
    {
        ForecastRequestedConsumer = new KafkaTestConsumer<ForecastRequestedEvent>(
            kafkaOptions.BrokersFlat,
            kafkaOptions.SchemaRegistry.Url,
            topicsOptions.ForecastRequested);
        _kafkaTestConsumers.Add(ForecastRequestedConsumer);
    }

    public void Dispose()
    {
        foreach (var kafkaTestConsumer in _kafkaTestConsumers)
        {
            kafkaTestConsumer.Dispose();
        }
    }
}
