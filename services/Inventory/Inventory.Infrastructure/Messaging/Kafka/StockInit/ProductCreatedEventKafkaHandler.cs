using Inventory.Application.Common.Data;
using Inventory.Application.StockItems.InitializeStockItem;
using Inventory.Infrastructure.Messaging.Kafka.SagaCommands;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using AvroProductCreatedEvent = Catalog.Products.ProductCreatedEvent;

namespace Inventory.Infrastructure.Messaging.Kafka.StockInit;

/// <summary>
/// Consumes Catalog's <c>ProductCreatedEvent</c> on <c>catalog.products</c>
/// and initializes a fresh event-sourced stream for the new product.
/// Idempotent against re-delivery: the application handler checks
/// <c>Version &gt; 0</c> and returns <c>Result.Ok</c> with no event when the
/// stream is already initialized
/// (<see cref="InitializeStockItemCommandHandler"/>). The KafkaFlow inbox
/// middleware (message-id dedup) is the first defense; the version-guard is
/// the second; together they keep duplicate Catalog deliveries safe.
/// </summary>
/// <remarks>
/// Reuses <see cref="SagaCommandHandlerBase{T}"/> for the transactional
/// envelope + DLT routing semantics, even though this is a domain event
/// rather than a saga command — the wrapper's contract (one tx around the
/// dispatch + DLT on <c>Result.Fail</c>) matches what we want here too.
/// </remarks>
internal sealed class ProductCreatedEventKafkaHandler
    : SagaCommandHandlerBase<AvroProductCreatedEvent>, IMessageHandler<AvroProductCreatedEvent>
{
    private readonly ICommandHandler<InitializeStockItemCommand> _appHandler;

    public ProductCreatedEventKafkaHandler(
        ICommandHandler<InitializeStockItemCommand> appHandler,
        ITransactionalOutbox<IInventoryDbContext> transactionalOutbox,
        ILogger<ProductCreatedEventKafkaHandler> logger)
        : base(transactionalOutbox, logger)
    {
        _appHandler = appHandler;
    }

    public Task Handle(IMessageContext context, AvroProductCreatedEvent message) =>
        ExecuteAsync(
            context,
            new Dictionary<string, object?>
            {
                ["ProductId"] = message.ProductId,
                ["Sku"] = message.Sku,
                ["EventCreatedAtUtc"] = message.CreatedAtUtc,
            },
            ct => _appHandler.HandleAsync(
                new InitializeStockItemCommand
                {
                    ProductId = message.ProductId,
                    // Deliberate deviation from ADR-0015's TimeProvider discipline:
                    // OccurredOnUtc is Catalog's product-creation moment, not the
                    // moment Inventory observed it. This preserves the original
                    // t0 of the stream across re-deliveries — using
                    // TimeProvider.GetUtcNow() here would mean two replays of
                    // the same ProductCreatedEvent produce different stream
                    // start times, breaking historical reconstruction.
                    OccurredOnUtc = new DateTimeOffset(
                        DateTime.SpecifyKind(message.CreatedAtUtc, DateTimeKind.Utc),
                        TimeSpan.Zero),
                },
                ct));
}
