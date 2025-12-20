using System.Diagnostics;
using Avro.Specific;
using DotNetAtlas.Outbox.Core;
using DotNetAtlas.Outbox.EntityFrameworkCore.Common;
using DotNetAtlas.Outbox.EntityFrameworkCore.Core;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Outbox.EntityFrameworkCore.EntityFramework;

/// <summary>
/// Scoped implementation of <see cref="ITransactionalOutbox"/> that serializes
/// Avro events and adds them to the outbox table using the injected DbContext.
/// </summary>
/// <remarks>
/// This service must be registered as Scoped to ensure it shares the same
/// DbContext instance as the domain event handlers, maintaining transactional consistency.
/// </remarks>
internal sealed class TransactionalOutbox<TContext> : ITransactionalOutbox
    where TContext : IOutboxDbContext
{
    private readonly TContext _dbContext;
    private readonly AvroSerializer _avroSerializer;
    private readonly TimeProvider _timeProvider;
    private readonly string? _messageOrigin;

    public TransactionalOutbox(
        TContext dbContext,
        AvroSerializer avroSerializer,
        TimeProvider timeProvider,
        IOptions<TransactionalOutboxOptions> options)
    {
        _dbContext = dbContext;
        _avroSerializer = avroSerializer;
        _timeProvider = timeProvider;
        _messageOrigin = options.Value.MessageOrigin;
    }

    public void Publish(string kafkaKey, ISpecificRecord integrationEvent)
    {
        var messageType = integrationEvent.GetType();
        var bytes = _avroSerializer.Serialize(integrationEvent, messageType);

        var activity = Activity.Current;
        var headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(activity) ?? [];

        if (!string.IsNullOrEmpty(_messageOrigin))
        {
            headers[OutboxMessageHeaders.Origin] = _messageOrigin;
        }

        headers[OutboxMessageHeaders.MessageId] = Guid.CreateVersion7().ToString();

        var outboxMessage = new OutboxMessage
        {
            KafkaKey = kafkaKey,
            AvroPayload = bytes,
            Type = messageType.FullName ?? messageType.Name,
            CreatedUtc = _timeProvider.GetUtcNow(),
            Headers = OutboxMessageHeaderExtensions.SerializeHeaders(headers)
        };

        _dbContext.OutboxMessages.Add(outboxMessage);
    }
}
