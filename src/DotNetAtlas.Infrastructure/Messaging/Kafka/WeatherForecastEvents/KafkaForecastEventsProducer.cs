using DotNetAtlas.Application.WeatherForecast.Common.Abstractions;
using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.DomainToAvroMappings;
using KafkaFlow;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Infrastructure.Messaging.Kafka.WeatherForecastEvents;

public class KafkaForecastEventsProducer : IForecastEventsProducer
{
    private readonly IMessageProducer<KafkaForecastEventsProducer> _producer;
    private readonly string _topicName;

    public KafkaForecastEventsProducer(
        IMessageProducer<KafkaForecastEventsProducer> producer,
        IOptions<TopicsOptions> topicOptions)
    {
        _producer = producer;
        _topicName = topicOptions.Value.ForecastRequested;
    }

    public async Task PublishForecastRequestedAsync(GetForecastQuery message)
    {
        var forecastRequestedEvent = message.ToForecastRequest();

        await _producer.ProduceAsync(_topicName, null, forecastRequestedEvent);
    }
}
