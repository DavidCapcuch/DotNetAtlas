using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Domain.Products.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Catalog.Application.Products.CreateProduct;

/// <summary>
/// Publishes the external product-created event to the <c>catalog.products</c> topic via the
/// transactional outbox on every <see cref="ProductCreatedDomainEvent"/>. The event carries fields
/// the internal domain event doesn't hold, so the handler loads
/// <see cref="Catalog.Domain.Products.Product"/> and its
/// <see cref="Catalog.Domain.Categories.Category"/> from the DbContext (free lookup via the EF Core
/// change tracker) and delegates the shape to <see cref="ProductCreatedMapper"/>.
/// </summary>
public sealed class ProductCreatedOutboxPublisherDomainEventHandler : IDomainEventHandler<ProductCreatedDomainEvent>
{
    private readonly ICatalogDbContext _db;
    private readonly ITransactionalOutbox<ICatalogDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<ProductCreatedOutboxPublisherDomainEventHandler> _logger;

    public ProductCreatedOutboxPublisherDomainEventHandler(
        ICatalogDbContext db,
        ITransactionalOutbox<ICatalogDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<ProductCreatedOutboxPublisherDomainEventHandler> logger)
    {
        _db = db;
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public async Task Handle(ProductCreatedDomainEvent domainEvent, CancellationToken ct)
    {
        var product = await _db.Products.FindAsync([domainEvent.ProductId], ct)
            ?? throw new DataIntegrityException(
                "Catalog.OutboxMissingProduct",
                $"Product '{domainEvent.ProductId}' not found when publishing ProductCreatedEvent.");

        var category = await _db.Categories.FindAsync([product.CategoryId], ct)
            ?? throw new DataIntegrityException(
                "Catalog.OutboxMissingCategory",
                $"Category '{product.CategoryId}' not found when publishing ProductCreatedEvent.");

        var avro = product.ToProductCreatedEvent(category);

        _outbox.AddOutboxMessage(_topics.CatalogProducts, product.Id.ToString(), avro);

        _logger.LogDebug(
            "Enqueued ProductCreatedEvent for {ProductId} on topic {Topic}",
            product.Id, _topics.CatalogProducts);
    }
}
