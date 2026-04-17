using KafkaFlow;
using Microsoft.Extensions.Logging;
using Order.AlertSubscriptions;
using Ordering.Application.AlertSubscriptions.CompleteAlertSubscriptionOrder;
using Platform.CQRS;

namespace Ordering.Infrastructure.Messaging.Kafka.Handlers;

/// <summary>
/// Handles <see cref="AlertSubscriptionExtensionCompletedEvent"/> from the Extension Saga.
/// Marks the corresponding order as completed.
/// </summary>
public sealed class ExtensionCompletedKafkaHandler : IMessageHandler<AlertSubscriptionExtensionCompletedEvent>
{
    private readonly ICommandHandler<CompleteAlertSubscriptionOrderCommand> _commandHandler;
    private readonly ILogger<ExtensionCompletedKafkaHandler> _logger;

    public ExtensionCompletedKafkaHandler(
        ICommandHandler<CompleteAlertSubscriptionOrderCommand> commandHandler,
        ILogger<ExtensionCompletedKafkaHandler> logger)
    {
        _commandHandler = commandHandler;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AlertSubscriptionExtensionCompletedEvent message)
    {
        _logger.LogDebug(
            "Received AlertSubscriptionExtensionCompletedEvent for CorrelationId: {CorrelationId}, UserId: {UserId}",
            message.CorrelationId, message.UserId);

        var cancellationToken = context.ConsumerContext.WorkerStopped;

        var result = await _commandHandler.HandleAsync(
            new CompleteAlertSubscriptionOrderCommand { OrderId = message.CorrelationId },
            cancellationToken);

        if (result.IsFailed)
        {
            throw new InvalidOperationException(
                $"Failed to complete extension order {message.CorrelationId}: " +
                $"{string.Join(", ", result.Errors)}");
        }

        _logger.LogInformation(
            "Extension order completed for CorrelationId {CorrelationId}, UserId {UserId}",
            message.CorrelationId, message.UserId);
    }
}
