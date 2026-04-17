using KafkaFlow;
using Microsoft.Extensions.Logging;
using Order.AlertSubscriptions;
using Ordering.Application.AlertSubscriptions.FailAlertSubscriptionOrder;
using Platform.CQRS;

namespace Ordering.Infrastructure.Messaging.Kafka.Handlers;

/// <summary>
/// Handles <see cref="AlertSubscriptionExtensionFailedEvent"/> from the Extension Saga.
/// Marks the corresponding order as failed.
/// </summary>
public sealed class ExtensionFailedKafkaHandler : IMessageHandler<AlertSubscriptionExtensionFailedEvent>
{
    private readonly ICommandHandler<FailAlertSubscriptionOrderCommand> _commandHandler;
    private readonly ILogger<ExtensionFailedKafkaHandler> _logger;

    public ExtensionFailedKafkaHandler(
        ICommandHandler<FailAlertSubscriptionOrderCommand> commandHandler,
        ILogger<ExtensionFailedKafkaHandler> logger)
    {
        _commandHandler = commandHandler;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AlertSubscriptionExtensionFailedEvent message)
    {
        _logger.LogDebug(
            "Received AlertSubscriptionExtensionFailedEvent for CorrelationId: {CorrelationId}, " +
            "ErrorCode: {ErrorCode}, CompensationTriggered: {CompensationTriggered}",
            message.CorrelationId, message.ErrorCode, message.CompensationTriggered);

        var cancellationToken = context.ConsumerContext.WorkerStopped;

        var result = await _commandHandler.HandleAsync(
            new FailAlertSubscriptionOrderCommand { OrderId = message.CorrelationId },
            cancellationToken);

        if (result.IsFailed)
        {
            throw new InvalidOperationException(
                $"Failed to fail extension order {message.CorrelationId}: " +
                $"{string.Join(", ", result.Errors)}");
        }

        _logger.LogInformation(
            "Extension order marked as failed for CorrelationId {CorrelationId}, UserId {UserId}, ErrorCode: {ErrorCode}",
            message.CorrelationId, message.UserId, message.ErrorCode);
    }
}
