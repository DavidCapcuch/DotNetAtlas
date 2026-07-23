using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Domain.Products.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Catalog.Application.Products.DiscontinueProduct;

/// <summary>
/// Loads the discontinued product and enqueues the mapped external event via the transactional
/// outbox on every <see cref="ProductDiscontinuedDomainEvent"/>. Runs inside the command's UoW so
/// the outbox row commits with the aggregate write (CQRS-on-Postgres atomicity per catalog.md § 9).
/// Field mapping lives in <see cref="ProductDiscontinuedMapper"/>.
/// </summary>
public sealed class ProductDiscontinuedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<ProductDiscontinuedDomainEvent>
{
    private readonly ICatalogDbContext _db;
    private readonly ITransactionalOutbox<ICatalogDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<ProductDiscontinuedOutboxPublisherDomainEventHandler> _logger;

    public ProductDiscontinuedOutboxPublisherDomainEventHandler(
        ICatalogDbContext db,
        ITransactionalOutbox<ICatalogDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<ProductDiscontinuedOutboxPublisherDomainEventHandler> logger)
    {
        _db = db;
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public async Task Handle(ProductDiscontinuedDomainEvent domainEvent, CancellationToken ct)
    {
        var product = await _db.Products.FindAsync([domainEvent.ProductId], ct)
            ?? throw new DataIntegrityException(
                "Catalog.OutboxMissingProduct",
                $"Product '{domainEvent.ProductId}' not found when publishing ProductDiscontinuedEvent.");

        var avro = product.ToProductDiscontinuedEvent(domainEvent);

        _outbox.AddOutboxMessage(_topics.CatalogProducts, product.Id.ToString(), avro);

        _logger.LogDebug(
            "Enqueued ProductDiscontinuedEvent for {ProductId} on topic {Topic}",
            product.Id, _topics.CatalogProducts);
    }
}
