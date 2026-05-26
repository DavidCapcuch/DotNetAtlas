using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Domain.Categories.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;
using AvroCategoryCreatedEvent = Catalog.Categories.CategoryCreatedEvent;

namespace Catalog.Application.Categories.CreateCategory;

public sealed class CategoryCreatedOutboxPublisher
    : IDomainEventHandler<CategoryCreatedDomainEvent>
{
    private readonly ICatalogDbContext _db;
    private readonly ITransactionalOutbox<ICatalogDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<CategoryCreatedOutboxPublisher> _logger;

    public CategoryCreatedOutboxPublisher(
        ICatalogDbContext db,
        ITransactionalOutbox<ICatalogDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<CategoryCreatedOutboxPublisher> logger)
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

        var avro = new AvroCategoryCreatedEvent
        {
            CategoryId = category.Id,
            Name = category.Name,
            ParentCategoryId = category.ParentCategoryId,
            Path = category.Path.Value,
            CreatedAtUtc = category.CreatedUtc.UtcDateTime,
        };

        _outbox.AddOutboxMessage(_topics.CatalogCategories, category.Id.ToString(), avro);

        _logger.LogDebug(
            "Enqueued CategoryCreatedEvent for {CategoryId} on topic {Topic}",
            category.Id, _topics.CatalogCategories);
    }
}
