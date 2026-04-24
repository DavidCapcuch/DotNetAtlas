using KafkaFlow;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppCancelOrderCommand = Ordering.Application.Orders.CancelOrder.CancelOrderCommand;
using AvroCancelOrderCommand = Ordering.Orders.CancelOrderCommand;

namespace Ordering.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Consumes the saga-issued <c>CancelOrderCommand</c> on
/// <c>ordering.order-commands</c> and dispatches it to the application
/// handler with <c>IsAdmin=true</c> — the saga is a privileged caller.
/// </summary>
internal sealed class CancelOrderCommandKafkaHandler
    : SagaCommandHandlerBase<AvroCancelOrderCommand>, IMessageHandler<AvroCancelOrderCommand>
{
    private readonly ICommandHandler<AppCancelOrderCommand> _appHandler;

    public CancelOrderCommandKafkaHandler(
        ICommandHandler<AppCancelOrderCommand> appHandler,
        ITransactionalOutbox<IOrderingDbContext> transactionalOutbox,
        ILogger<CancelOrderCommandKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroCancelOrderCommand message) =>
        ExecuteAsync(context, message.CorrelationId, message.OrderId,
            ct => _appHandler.HandleAsync(message.ToAppCommand(), ct));
}
