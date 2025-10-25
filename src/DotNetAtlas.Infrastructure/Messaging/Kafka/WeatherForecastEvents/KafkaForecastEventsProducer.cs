using DotNetAtlas.Application.WeatherForecast.Common.Abstractions;
using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;
using KafkaFlow;
using Microsoft.Extensions.Options;
using Weather.Contracts;

namespace DotNetAtlas.Infrastructure.Messaging.Kafka.WeatherForecastEvents;

public class KafkaForecastEventsProducer : IForecastEventsProducer
{
    private readonly IMessageProducer<KafkaForecastEventsProducer> _producer;
    private readonly TimeProvider _timeProvider;
    private readonly string _topicName;

    public KafkaForecastEventsProducer(
        IMessageProducer<KafkaForecastEventsProducer> producer,
        TimeProvider timeProvider,
        IOptions<TopicsOptions> topicOptions)
    {
        _producer = producer;
        _timeProvider = timeProvider;
        _topicName = topicOptions.Value.ForecastRequested;
    }

    public async Task PublishForecastRequestedAsync(GetForecastQuery message, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var forecastRequestedEvent = new ForecastRequestedEvent
        {
            City = message.City,
            CountryCode = (CountryCode)message.CountryCode,
            Days = message.Days,
            UserId = message.UserId,
            RequestedAtUtc = utcNow
        };

        await _producer.ProduceAsync(_topicName, null, forecastRequestedEvent);
    }
}
