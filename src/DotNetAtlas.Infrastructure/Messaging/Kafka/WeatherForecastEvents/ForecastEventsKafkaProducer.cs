using System.Diagnostics.CodeAnalysis;
using DotNetAtlas.Application.Common.Messaging;
using DotNetAtlas.Application.WeatherForecast.Common;
using DotNetAtlas.Domain.Forecast.ValueObjects;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Infrastructure.Messaging.Kafka.WeatherForecastEvents;

public class ForecastEventsKafkaProducer : IForecastEventsProducer
{
    private readonly IMessageProducer<ForecastEventsKafkaProducer> _producer;
    private readonly ILogger<ForecastEventsKafkaProducer> _logger;
    private readonly string _topicName;
    private readonly TimeProvider _timeProvider;

    public ForecastEventsKafkaProducer(
        IMessageProducer<ForecastEventsKafkaProducer> producer,
        IOptions<TopicsOptions> topicOptions,
        ILogger<ForecastEventsKafkaProducer> logger,
        TimeProvider timeProvider)
    {
        _producer = producer;
        _logger = logger;
        _timeProvider = timeProvider;
        _topicName = topicOptions.Value.ForecastRequested;
    }

    [SuppressMessage("Performance", "CA1849:Volání asynchronních metod v asynchronní metodě",
        Justification = "Sync Produce() has much bigger throughput")]
    public Task PublishForecastRequestedFireAndForgetAsync(ForecastCriteria forecastCriteria, Guid? userId)
    {
        var forecastRequestedEvent =
            forecastCriteria.ToForecastRequestedEvent(userId, _timeProvider.GetUtcNow().UtcDateTime);

        _producer.Produce(_topicName, null, forecastRequestedEvent, null, report =>
        {
            if (report.Error is not null)
            {
                _logger.LogError(
                    "Failed to deliver ForecastRequestedEvent to Kafka. " +
                    "City: {City}, CountryCode: {CountryCode}, Error: {Error}",
                    forecastCriteria.City.Name, forecastCriteria.CountryCode, report.Error.Reason);
            }
            else
            {
                _logger.LogDebug(
                    "Successfully delivered ForecastRequestedEvent to Kafka. " +
                    "City: {City}, CountryCode: {CountryCode}, Partition: {Partition}, Offset: {Offset}",
                    forecastCriteria.City.Name, forecastCriteria.CountryCode, report.Partition, report.Offset);
            }
        });

        return Task.CompletedTask;
    }
}
