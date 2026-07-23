using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Domain.Categories.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Catalog.Application.Categories.CreateCategory;

public sealed class CategoryCreatedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<CategoryCreatedDomainEvent>
{
    private readonly ICatalogDbContext _db;
    private readonly ITransactionalOutbox<ICatalogDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<CategoryCreatedOutboxPublisherDomainEventHandler> _logger;

    public CategoryCreatedOutboxPublisherDomainEventHandler(
        ICatalogDbContext db,
        ITransactionalOutbox<ICatalogDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<CategoryCreatedOutboxPublisherDomainEventHandler> logger)
    {
        _db = db;
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public async Task Handle(CategoryCreatedDomainEvent domainEvent, CancellationToken ct)
    {
        var category = await _db.Categories.FindAsync([domainEvent.CategoryId], ct)
            ?? throw new DataIntegrityException(
                "Catalog.OutboxMissingCategory",
                $"Category '{domainEvent.CategoryId}' not found when publishing CategoryCreatedEvent.");

        var avro = category.ToCategoryCreatedEvent();

        _outbox.AddOutboxMessage(_topics.CatalogCategories, category.Id.ToString(), avro);

        _logger.LogDebug(
            "Enqueued CategoryCreatedEvent for {CategoryId} on topic {Topic}",
            category.Id, _topics.CatalogCategories);
    }
}
