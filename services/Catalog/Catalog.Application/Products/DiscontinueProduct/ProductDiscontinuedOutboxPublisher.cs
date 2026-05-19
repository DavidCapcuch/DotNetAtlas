using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Domain.Products.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;
using AvroProductDiscontinuedEvent = Catalog.Products.ProductDiscontinuedEvent;

namespace Catalog.Application.Products.DiscontinueProduct;

/// <summary>
/// Domain-event handler that translates <see cref="ProductDiscontinuedDomainEvent"/> into
/// the external <see cref="AvroProductDiscontinuedEvent"/> shape and enqueues it via the
/// transactional outbox. Runs inside the command's UoW so the outbox row commits with the
/// aggregate write (CQRS-on-Postgres atomicity per catalog.md § 9).
/// </summary>
public sealed class ProductDiscontinuedOutboxPublisher
    : IDomainEventHandler<ProductDiscontinuedDomainEvent>
{
    private readonly ICatalogDbContext _db;
    private readonly ITransactionalOutbox<ICatalogDbContext> _outbox;
    private readonly CatalogTopicsOptions _topics;
    private readonly ILogger<ProductDiscontinuedOutboxPublisher> _logger;

    public ProductDiscontinuedOutboxPublisher(
        ICatalogDbContext db,
        ITransactionalOutbox<ICatalogDbContext> outbox,
        IOptions<CatalogTopicsOptions> topics,
        ILogger<ProductDiscontinuedOutboxPublisher> logger)
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

        var avro = new AvroProductDiscontinuedEvent
        {
            ProductId = product.Id,
            Sku = product.Sku.Value,
            Reason = domainEvent.Reason,
            DiscontinuedAtUtc = domainEvent.OccurredOnUtc.UtcDateTime,
        };

        _outbox.AddOutboxMessage(_topics.CatalogProducts, product.Id.ToString(), avro);

        _logger.LogDebug(
            "Enqueued ProductDiscontinuedEvent for {ProductId} on topic {Topic}",
            product.Id, _topics.CatalogProducts);
    }
}
