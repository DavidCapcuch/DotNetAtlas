using System.Diagnostics;
using DotNetAtlas.Outbox.Core;
using DotNetAtlas.Outbox.EntityFrameworkCore.Core;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DotNetAtlas.Outbox.EntityFrameworkCore.EntityFramework;

/// <summary>
/// EF Core interceptor that automatically persists domain events to the outbox table.
/// Captures distributed tracing context into headers for async trace continuity.
/// </summary>
internal sealed class OutboxInterceptor : SaveChangesInterceptor
{
    private readonly AvroSerializer _avroSerializer;
    private readonly DomainEventExtractionCache _domainEventExtractionCache;
    private readonly AvroMappingCache _avroMappingCache;
    private readonly TimeProvider _timeProvider;

    public OutboxInterceptor(
        AvroSerializer avroSerializer,
        DomainEventExtractionCache domainEventExtractionCache,
        AvroMappingCache avroMappingCache,
        TimeProvider timeProvider)
    {
        _avroSerializer = avroSerializer;
        _domainEventExtractionCache = domainEventExtractionCache;
        _avroMappingCache = avroMappingCache;
        _timeProvider = timeProvider;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var dbContext = eventData.Context;
        if (dbContext is not IOutboxDbContext context)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var activity = Activity.Current;
        var headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(activity);
        var serializedHeaders = headers != null
            ? OutboxMessageHeaderExtensions.SerializeHeaders(headers)
            : null;

        var outboxMessages = new List<OutboxMessage>();
        var utcNow = _timeProvider.GetUtcNow();
        foreach (var entityEntry in dbContext.ChangeTracker.Entries())
        {
            if (_domainEventExtractionCache.TryExtract(entityEntry.Entity, out var aggregateData))
            {
                var domainEvents = aggregateData.DomainEvents;
                foreach (var domainEvent in domainEvents)
                {
                    var avro = _avroMappingCache.MapToAvro(domainEvent);
                    if (avro != null)
                    {
                        var messageType = avro.GetType();
                        var bytes = _avroSerializer.Serialize(avro, messageType);
                        outboxMessages.Add(
                            new OutboxMessage
                            {
                                KafkaKey = aggregateData.KafkaKey,
                                AvroPayload = bytes,
                                Type = messageType.Name,
                                CreatedUtc = utcNow,
                                Headers = serializedHeaders
                            });
                    }
                }
            }
        }

        context.OutboxMessages.AddRange(outboxMessages);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
