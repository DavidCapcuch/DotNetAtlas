using System.Text;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Config;
using DotNetAtlas.Messaging.Abstractions;
using KafkaFlow;
using Microsoft.Extensions.Options;
using Weather.Alerts;

namespace DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;

/// <summary>
/// Kafka producer for dev/testing commands that simulate saga orchestrators.
/// Used by dev endpoints to publish commands that would normally come from
/// the Purchase Saga or Extension Saga.
/// </summary>
public class DevEventsKafkaProducer
{
    private readonly IMessageProducer<DevEventsKafkaProducer> _producer;
    private readonly string _weatherAlertsCommandsTopic;

    public DevEventsKafkaProducer(
        IMessageProducer<DevEventsKafkaProducer> producer,
        IOptions<TopicsOptions> topicOptions)
    {
        _producer = producer;
        _weatherAlertsCommandsTopic = topicOptions.Value.WeatherAlertsCommands;
    }

    /// <summary>
    /// Publishes an ActivateSubscriptionCommand to simulate the Purchase Saga.
    /// </summary>
    public Task PublishActivateSubscriptionCommandAsync(ActivateSubscriptionCommand command)
    {
        return _producer.ProduceAsync(
            _weatherAlertsCommandsTopic, command.UserId.ToString(), command);
    }

    /// <summary>
    /// Publishes an ExtendSubscriptionCommand to simulate the Extension Saga.
    /// </summary>
    public Task PublishExtendSubscriptionCommandAsync(ExtendSubscriptionCommand command)
    {
        return _producer
            .ProduceAsync(_weatherAlertsCommandsTopic, command.UserId.ToString(), command);
    }

    /// <summary>
    /// Publishes an ExtendSubscriptionCommand with a specific message ID.
    /// Used for testing idempotency by simulating Kafka redeliveries with the same message ID.
    /// </summary>
    public Task PublishExtendSubscriptionCommandWithMessageIdAsync(
        ExtendSubscriptionCommand command,
        Guid messageId)
    {
        var headers = new MessageHeaders
        {
            {
                MessageHeaderKeys.MessageId, Encoding.UTF8.GetBytes(messageId.ToString())
            }
        };

        return _producer
            .ProduceAsync(
                _weatherAlertsCommandsTopic,
                command.UserId.ToString(),
                command,
                headers);
    }
}
