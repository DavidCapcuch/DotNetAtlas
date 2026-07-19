using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Domain.Products.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;
using AvroProductPriceChanged = Catalog.Products.ProductPriceChangedEvent;

namespace Catalog.Application.Products.UpdateProductPrice;

public sealed class ProductPriceChangedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<ProductPriceChangedDomainEvent>
{
    /// <summary>
    /// Scale pinned by the money fields in <c>ProductPriceChangedEvent.avsc</c>
    /// (<c>decimal(19,4)</c>). Avro rejects a datum whose scale differs from the schema's, so
    /// amounts must be normalised rather than inheriting the .NET decimal's own scale.
    /// </summary>
    private const int MoneyScale = 4;

    private readonly ICatalogDbContext _db;
    private readonly ITransactionalOutbox<ICatalogDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<ProductPriceChangedOutboxPublisherDomainEventHandler> _logger;

    public ProductPriceChangedOutboxPublisherDomainEventHandler(
        ICatalogDbContext db,
        ITransactionalOutbox<ICatalogDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<ProductPriceChangedOutboxPublisherDomainEventHandler> logger)
    {
        _db = db;
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public async Task Handle(ProductPriceChangedDomainEvent domainEvent, CancellationToken ct)
    {
        var product = await _db.Products.FindAsync([domainEvent.ProductId], ct)
            ?? throw new DataIntegrityException(
                "Catalog.OutboxMissingProduct",
                $"Product '{domainEvent.ProductId}' not found when publishing ProductPriceChangedEvent.");

        var avro = new AvroProductPriceChanged
        {
            ProductId = product.Id,
            Sku = product.Sku.Value,
            OldPriceAmount = domainEvent.OldPrice.Amount.ToAvroDecimal(MoneyScale),
            NewPriceAmount = domainEvent.NewPrice.Amount.ToAvroDecimal(MoneyScale),
            Currency = domainEvent.NewPrice.Currency.Name,
            ChangedAtUtc = domainEvent.OccurredOnUtc.UtcDateTime,
        };

        _outbox.AddOutboxMessage(_topics.CatalogProducts, product.Id.ToString(), avro);

        _logger.LogDebug(
            "Enqueued ProductPriceChangedEvent for {ProductId} on topic {Topic}",
            product.Id, _topics.CatalogProducts);
    }
}
