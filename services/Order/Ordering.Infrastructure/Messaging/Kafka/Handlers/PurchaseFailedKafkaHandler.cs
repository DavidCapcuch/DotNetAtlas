using KafkaFlow;
using Microsoft.Extensions.Logging;
using Order.AlertSubscriptions;
using Ordering.Application.AlertSubscriptions.FailAlertSubscriptionOrder;
using Platform.CQRS;

namespace Ordering.Infrastructure.Messaging.Kafka.Handlers;

/// <summary>
/// Handles <see cref="AlertSubscriptionPurchaseFailedEvent"/> from the Purchase Saga.
/// Marks the corresponding order as failed.
/// </summary>
public sealed class PurchaseFailedKafkaHandler : IMessageHandler<AlertSubscriptionPurchaseFailedEvent>
{
    private readonly ICommandHandler<FailAlertSubscriptionOrderCommand> _commandHandler;
    private readonly ILogger<PurchaseFailedKafkaHandler> _logger;

    public PurchaseFailedKafkaHandler(
        ICommandHandler<FailAlertSubscriptionOrderCommand> commandHandler,
        ILogger<PurchaseFailedKafkaHandler> logger)
    {
        _commandHandler = commandHandler;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AlertSubscriptionPurchaseFailedEvent message)
    {
        _logger.LogDebug(
            "Received AlertSubscriptionPurchaseFailedEvent for CorrelationId: {CorrelationId}, " +
            "ErrorCode: {ErrorCode}, CompensationTriggered: {CompensationTriggered}",
            message.CorrelationId, message.ErrorCode, message.CompensationTriggered);

        var cancellationToken = context.ConsumerContext.WorkerStopped;

        var result = await _commandHandler.HandleAsync(
            new FailAlertSubscriptionOrderCommand { OrderId = message.CorrelationId },
            cancellationToken);

        if (result.IsFailed)
        {
            throw new InvalidOperationException(
                $"Failed to fail order {message.CorrelationId}: " +
                $"{string.Join(", ", result.Errors)}");
        }

        _logger.LogInformation(
            "Order marked as failed for CorrelationId {CorrelationId}, UserId {UserId}, ErrorCode: {ErrorCode}",
            message.CorrelationId, message.UserId, message.ErrorCode);
    }
}
