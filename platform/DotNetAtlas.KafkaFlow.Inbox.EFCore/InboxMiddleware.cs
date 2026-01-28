using System.Collections.Frozen;
using DotNetAtlas.KafkaFlow.Inbox.EFCore.Common;
using DotNetAtlas.Messaging.Abstractions;
using DotNetAtlas.ReliableMessaging.Inbox.Core;
using DotNetAtlas.ReliableMessaging.Inbox.EFCore;
using EntityFramework.Exceptions.Common;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.KafkaFlow.Inbox.EFCore;

/// <summary>
/// KafkaFlow consumer middleware that provides inbox pattern idempotency.
/// Deduplicates messages based on the <c>message.id</c> header.
/// </summary>
/// <remarks>
/// <para>
/// This middleware should be placed after retry middleware in the consumer pipeline
/// so that transient failures are retried before being marked as processed.
/// </para>
/// <para>
/// The middleware extracts the message ID from the <see cref="MessageHeaderKeys.MessageId"/> header.
/// If no header is present, a <see cref="InboxMissingMessageIdException"/> is thrown since all messages
/// processed by the inbox middleware must have valid message IDs.
/// </para>
/// <para>
/// Message processing and inbox recording are wrapped in a single transaction.
/// If any error occurs (including handler failures), the transaction is rolled back.
/// </para>
/// </remarks>
internal sealed class InboxMiddleware : IMessageMiddleware
{
    private readonly IInboxDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly FrozenSet<Type> _inboxEnabledMessageTypes;
    private readonly ILogger<InboxMiddleware> _logger;

    public InboxMiddleware(
        IInboxDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<InboxMiddleware> logger,
        IEnumerable<Type> inboxEnabledMessageTypes)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
        _inboxEnabledMessageTypes = inboxEnabledMessageTypes.ToFrozenSet();
    }

    public async Task Invoke(IMessageContext context, MiddlewareDelegate next)
    {
        var messageType = context.Message.Value?.GetType();
        if (!IsInboxEnabledFor(messageType))
        {
            await next(context);
            return;
        }

        var messageId = context.ExtractMessageId()
                        ?? throw new InboxMissingMessageIdException(
                            $"Message with key {context.Message.Key?.ToString() ?? "null"} on topic {context.ConsumerContext.Topic} " +
                            $"partition {context.ConsumerContext.Partition} offset {context.ConsumerContext.Offset} " +
                            $"has no valid {MessageHeaderKeys.MessageId} header. " +
                            $"All messages processed by the inbox middleware must have a valid {MessageHeaderKeys.MessageId} header.");

        var cancellationToken = context.ConsumerContext.WorkerStopped;

        // Check 1: Prevents unnecessary work most of the time.
        // However, if two consumers process the same message simultaneously, both may pass this check and proceed with work
        if (await HasMessageBeenProcessedAsync(messageId, cancellationToken))
        {
            _logger.LogInformation("Message {MessageId} already processed, skipping", messageId);
            return;
        }

        await ProcessWithInboxAsync(messageId, context, next, cancellationToken);
    }

    private async Task ProcessWithInboxAsync(
        Guid messageId,
        IMessageContext context,
        MiddlewareDelegate next,
        CancellationToken ct)
    {
        var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            // Transaction spans BOTH inbox insertion and message processing to ensure atomicity
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

            try
            {
                _dbContext.InboxMessages.Add(new InboxMessage
                {
                    MessageId = messageId,
                    ProcessedAtUtc = _timeProvider.GetUtcNow()
                });

                await _dbContext.SaveChangesAsync(ct);

                await next(context);

                await transaction.CommitAsync(ct);
            }
            catch (UniqueConstraintException ex)
                when (ex.Entries.Any(entry => entry.Entity is InboxMessage))
            {
                // Check 2: Database constraint ensures exactly-once processing. If two consumers passed Check 1,
                // only one can insert the InboxMessage record. The second hits the unique constraint and rolls back.
                await transaction.RollbackAsync(ct);

                _logger.LogInformation(ex,
                    "Message {MessageId} on {Topic}/{Partition}@{Offset} was concurrently processed, rollbacked duplicate",
                    messageId, context.ConsumerContext.Topic, context.ConsumerContext.Partition,
                    context.ConsumerContext.Offset);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private bool IsInboxEnabledFor(Type? messageType)
    {
        return messageType is not null && _inboxEnabledMessageTypes.Contains(messageType);
    }

    private async Task<bool> HasMessageBeenProcessedAsync(Guid messageId, CancellationToken ct)
    {
        return await _dbContext.InboxMessages.AnyAsync(inbox => inbox.MessageId == messageId, ct);
    }
}
