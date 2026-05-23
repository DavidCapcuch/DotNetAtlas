using Platform.Test.Framework.Kafka;
using Weather.Application.Common.Messaging;
using Weather.Forecast;
using Weather.Infrastructure.Messaging.Kafka.Config;

namespace Weather.IntegrationTests.Common;

/// <summary>
/// Centralized registry for managing Weather-specific Kafka test consumers in integration tests.
/// Lives in the Weather test assembly (not in Platform.Test.Framework) because it is hardwired
/// to <see cref="ForecastRequestedEvent"/> and Weather's <see cref="TopicsOptions"/>; the
/// platform test framework no longer references Weather, which was the cross-tier project ref
/// that broke saga CPM hygiene when MassTransit downgraded from v9 to v8.5.7.
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
