using Catalog.Application.Products.UpdateProductSellability;
using Inventory.Stock;
using KafkaFlow;
using Microsoft.Extensions.Logging;

namespace Catalog.Infrastructure.Messaging.Kafka.StockEvents;

/// <summary>
/// Inbound Kafka adapter for Inventory's <see cref="StockLevelChangedEvent"/> events. Translates the
/// Avro message into a call into the Application-layer
/// <see cref="IStockLevelChangedEventProjector"/>; the projection write itself lives in
/// Catalog.Application so architecture-tests.md § 2.1 holds across the Kafka-delivered,
/// inbox-deduped path.
/// </summary>
/// <remarks>
/// <para>
/// Inbox-dedup middleware (<c>Platform.KafkaFlow.Inbox.EFCore</c>) runs in front of this handler
/// — the same MessageId arriving twice is processed exactly once.
/// </para>
/// </remarks>
internal sealed class StockLevelChangedEventKafkaHandler : IMessageHandler<StockLevelChangedEvent>
{
    // Combine WorkerStopped with a per-message budget so a slow Postgres query during a
    // Kafka rebalance can't hold the partition until the worker stops — misbehaving
    // messages then starve other partitions.
    internal static readonly TimeSpan PerMessageBudget = TimeSpan.FromSeconds(30);

    private readonly IStockLevelChangedEventProjector _projector;

    public StockLevelChangedEventKafkaHandler(IStockLevelChangedEventProjector projector)
    {
        _projector = projector;
    }

    public async Task Handle(IMessageContext context, StockLevelChangedEvent message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        using var perMessageCts = CancellationTokenSource.CreateLinkedTokenSource(
            context.ConsumerContext.WorkerStopped);
        perMessageCts.CancelAfter(PerMessageBudget);

        await _projector.HandleAsync(message.ProductId, message.NewAvailable, perMessageCts.Token);
    }
}
