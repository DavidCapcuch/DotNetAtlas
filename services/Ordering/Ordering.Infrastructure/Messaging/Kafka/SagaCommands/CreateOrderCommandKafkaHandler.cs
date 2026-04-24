using FluentResults;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using AppCreateOrderCommand = Ordering.Application.Orders.CreateOrder.CreateOrderCommand;
using AvroCreateOrderCommand = Ordering.Orders.CreateOrderCommand;

namespace Ordering.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Consumes the saga-issued <c>CreateOrderCommand</c> on
/// <c>ordering.order-commands</c> and dispatches it to the application
/// handler. Idempotency is enforced by KafkaFlow inbox middleware
/// (message-id dedup) plus the handler's CorrelationId idempotency check.
/// </summary>
internal sealed class CreateOrderCommandKafkaHandler
    : SagaCommandHandlerBase<AvroCreateOrderCommand>, IMessageHandler<AvroCreateOrderCommand>
{
    private readonly ICommandHandler<AppCreateOrderCommand, Guid> _appHandler;

    public CreateOrderCommandKafkaHandler(
        ICommandHandler<AppCreateOrderCommand, Guid> appHandler,
        ITransactionalOutbox<IOrderingDbContext> transactionalOutbox,
        ILogger<CreateOrderCommandKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroCreateOrderCommand message) =>
        ExecuteAsync(context, message.CorrelationId, orderId: null, async ct =>
        {
            var appCommand = message.ToAppCommand();
            var result = await _appHandler.HandleAsync(appCommand, ct);
            return result.ToResult();
        });
}
