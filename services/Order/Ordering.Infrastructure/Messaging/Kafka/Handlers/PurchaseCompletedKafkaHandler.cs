using KafkaFlow;
using Microsoft.Extensions.Logging;
using Order.AlertSubscriptions;
using Ordering.Application.AlertSubscriptions.CompleteAlertSubscriptionOrder;
using Platform.CQRS;

namespace Ordering.Infrastructure.Messaging.Kafka.Handlers;

/// <summary>
/// Handles <see cref="AlertSubscriptionPurchaseCompletedEvent"/> from the Purchase Saga.
/// Marks the corresponding order as completed.
/// </summary>
public sealed class PurchaseCompletedKafkaHandler : IMessageHandler<AlertSubscriptionPurchaseCompletedEvent>
{
    private readonly ICommandHandler<CompleteAlertSubscriptionOrderCommand> _commandHandler;
    private readonly ILogger<PurchaseCompletedKafkaHandler> _logger;

    public PurchaseCompletedKafkaHandler(
        ICommandHandler<CompleteAlertSubscriptionOrderCommand> commandHandler,
        ILogger<PurchaseCompletedKafkaHandler> logger)
    {
        _commandHandler = commandHandler;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, AlertSubscriptionPurchaseCompletedEvent message)
    {
        _logger.LogDebug(
            "Received AlertSubscriptionPurchaseCompletedEvent for CorrelationId: {CorrelationId}, UserId: {UserId}",
            message.CorrelationId, message.UserId);

        var cancellationToken = context.ConsumerContext.WorkerStopped;

        var result = await _commandHandler.HandleAsync(
            new CompleteAlertSubscriptionOrderCommand { OrderId = message.CorrelationId },
            cancellationToken);

        if (result.IsFailed)
        {
            throw new InvalidOperationException(
                $"Failed to complete order {message.CorrelationId}: " +
                $"{string.Join(", ", result.Errors)}");
        }

        _logger.LogInformation(
            "Order completed for CorrelationId {CorrelationId}, UserId {UserId}",
            message.CorrelationId, message.UserId);
    }
}
