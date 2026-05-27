using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Domain.Products.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;
using AvroProductCreatedEvent = Catalog.Products.ProductCreatedEvent;
using AvroProductStatus = Catalog.Products.ProductStatus;
using DomainProductStatus = Catalog.Domain.Products.ValueObjects.ProductStatus;

namespace Catalog.Application.Products.CreateProduct;

/// <summary>
/// Publishes <see cref="AvroProductCreatedEvent"/> to the <c>catalog.products</c> topic via the
/// transactional outbox on every <see cref="ProductCreatedDomainEvent"/>. The external event
/// carries enriched fields (CategoryPath, Description, BrandName, Status, CreatedAtUtc) that the
/// internal domain event doesn't hold, so the publisher loads <see cref="Catalog.Domain.Products.Product"/>
/// and its <see cref="Catalog.Domain.Categories.Category"/> from the DbContext (free lookup via
/// the EF Core change tracker).
/// </summary>
public sealed class ProductCreatedOutboxPublisherDomainEventHandler : IDomainEventHandler<ProductCreatedDomainEvent>
{
    private const int MaxDescriptionLength = 1000;

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

        var avro = new AvroProductCreatedEvent
        {
            ProductId = product.Id,
            Sku = product.Sku.Value,
            Name = product.Name.Value,
            Description = TruncateDescription(product.Description.Value),
            CategoryId = product.CategoryId,
            CategoryPath = category.Path.Value,
            BrandName = product.Brand.Value,
            PriceAmount = new Avro.AvroDecimal(product.Price.Amount),
            PriceCurrency = product.Price.Currency.Name,
            Status = ToAvroStatus(product.Status),
            CreatedAtUtc = product.CreatedUtc.UtcDateTime,
        };

        _outbox.AddOutboxMessage(_topics.CatalogProducts, product.Id.ToString(), avro);

        _logger.LogDebug(
            "Enqueued ProductCreatedEvent for {ProductId} on topic {Topic}",
            product.Id, _topics.CatalogProducts);
    }

    private static string TruncateDescription(string description)
    {
        if (string.IsNullOrEmpty(description) || description.Length <= MaxDescriptionLength)
        {
            return description;
        }

        return description[..MaxDescriptionLength];
    }

    private static AvroProductStatus ToAvroStatus(DomainProductStatus status)
    {
        if (status == DomainProductStatus.Active)
        {
            return AvroProductStatus.Active;
        }

        if (status == DomainProductStatus.Discontinued)
        {
            return AvroProductStatus.Discontinued;
        }

        throw new DataIntegrityException(
            "Catalog.UnknownProductStatus",
            $"Unknown ProductStatus '{status.Name}' encountered during Avro mapping.");
    }
}
