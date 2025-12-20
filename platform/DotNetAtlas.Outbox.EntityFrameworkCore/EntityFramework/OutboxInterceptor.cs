using System.Diagnostics;
using DotNetAtlas.Outbox.Core;
using DotNetAtlas.Outbox.EntityFrameworkCore.Common;
using DotNetAtlas.Outbox.EntityFrameworkCore.Core;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DotNetAtlas.Outbox.EntityFrameworkCore.EntityFramework;

/// <summary>
/// EF Core interceptor that persists domain events to the outbox table as avro serialized messages.
/// Captures distributed tracing context into headers for async trace continuity.
/// Generates a unique MessageId and adds an Origin header for each message.
/// </summary>
internal sealed class OutboxInterceptor : SaveChangesInterceptor
{
    private readonly AvroSerializer _avroSerializer;
    private readonly DomainEventExtractionCache _domainEventExtractionCache;
    private readonly AvroMappingCache _avroMappingCache;
    private readonly TimeProvider _timeProvider;
    private readonly string? _messageOrigin;

    public OutboxInterceptor(
        AvroSerializer avroSerializer,
        DomainEventExtractionCache domainEventExtractionCache,
        AvroMappingCache avroMappingCache,
        TimeProvider timeProvider,
        string? messageOrigin)
    {
        _avroSerializer = avroSerializer;
        _domainEventExtractionCache = domainEventExtractionCache;
        _avroMappingCache = avroMappingCache;
        _timeProvider = timeProvider;
        _messageOrigin = messageOrigin;
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
        var baseHeaders = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(activity) ?? [];

        if (!string.IsNullOrEmpty(_messageOrigin))
        {
            baseHeaders[OutboxMessageHeaders.Origin] = _messageOrigin;
        }

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

                        var messageHeaders = new Dictionary<string, string>(baseHeaders)
                        {
                            [OutboxMessageHeaders.MessageId] = Guid.CreateVersion7().ToString()
                        };

                        outboxMessages.Add(
                            new OutboxMessage
                            {
                                KafkaKey = aggregateData.KafkaKey,
                                AvroPayload = bytes,
                                Type = messageType.FullName ?? messageType.Name,
                                CreatedUtc = utcNow,
                                Headers = OutboxMessageHeaderExtensions.SerializeHeaders(messageHeaders)
                            });
                    }
                }
            }
        }

        context.OutboxMessages.AddRange(outboxMessages);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
