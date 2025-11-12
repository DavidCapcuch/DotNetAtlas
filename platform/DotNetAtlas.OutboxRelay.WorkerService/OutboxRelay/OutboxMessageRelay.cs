using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using DotNetAtlas.Outbox.Core;
using DotNetAtlas.OutboxRelay.WorkerService.Observability.Metrics;
using DotNetAtlas.OutboxRelay.WorkerService.Observability.Tracing;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay;

public sealed class OutboxMessageRelay : IDisposable
{
    private const string LastSentIdCacheKey = "OutboxProcessor_LastSentId";

    private readonly IDbContextFactory<OutboxDbContext> _dbContextFactory;
    private readonly IProducer<string?, byte[]> _kafkaProducer;
    private readonly OutboxRelayOptions _outboxRelayOptions;
    private readonly ILogger<OutboxMessageRelay> _logger;
    private readonly OutboxRelayMetrics _outboxRelayMetrics;
    private readonly IMemoryCache _memoryCache;
    private bool _disposed;

    public OutboxMessageRelay(
        IDbContextFactory<OutboxDbContext> dbContextFactory,
        IProducer<string?, byte[]> kafkaProducer,
        IOptions<OutboxRelayOptions> options,
        ILogger<OutboxMessageRelay> logger,
        OutboxRelayMetrics outboxRelayMetrics,
        IMemoryCache memoryCache)
    {
        _dbContextFactory = dbContextFactory;
        _kafkaProducer = kafkaProducer;
        _outboxRelayOptions = options.Value;
        _logger = logger;
        _outboxRelayMetrics = outboxRelayMetrics;
        _memoryCache = memoryCache;
    }

    /// <summary>
    /// Publishes a batch of unpublished outbox messages with an "at least once" delivery guarantee.
    /// Only messages confirmed as delivered are deleted from the outbox.
    /// On error, tracks the earliest failed message ID and deletes all messages before it.
    /// </summary>
    /// <returns>True if all messages were successfully delivered and confirmed, false otherwise.</returns>
    public async Task<bool> PublishOutboxMessagesAsync(CancellationToken ct)
    {
        var publishedMessagesCount = 0;
        var publishedMessagesByType = new Dictionary<string, int>();
        var failedMessagesByType = new Dictionary<string, int>();

        var startTimestamp = Stopwatch.GetTimestamp();

        var maxDeleteId = 0L;
        var lastSentIdSnapshot = _memoryCache.Get<long>(LastSentIdCacheKey);
        var outboxDbContext = await _dbContextFactory.CreateDbContextAsync(ct);
        var outboxMessages = await outboxDbContext.OutboxMessages
            .Where(m => m.Id > lastSentIdSnapshot)
            .OrderBy(m => m.Id)
            .Take(_outboxRelayOptions.BatchSize)
            .ToListAsync(ct);

        if (outboxMessages.Count == 0)
        {
            await outboxDbContext.DisposeAsync();
            return true;
        }

        using var deliveryFailureTracker = new DeliveryFailureTracker(_logger, ct);
        foreach (var outboxMessage in outboxMessages)
        {
            if (deliveryFailureTracker.CancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Stopping message processing at message {MessageId} due to previous delivery failure",
                    outboxMessage.Id);
                break;
            }

            var messageType = string.Intern(outboxMessage.Type);
            try
            {
                PublishMessage(outboxMessage, deliveryFailureTracker);

                publishedMessagesCount++;
                publishedMessagesByType[messageType] = publishedMessagesByType.GetValueOrDefault(messageType, 0) + 1;
                maxDeleteId = outboxMessage.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to call Produce for outbox message {MessageId}", outboxMessage.Id);
                failedMessagesByType[messageType] = failedMessagesByType.GetValueOrDefault(messageType, 0) + 1;
                break;
            }
        }

        // Flush ensures all buffered messages are sent and delivery reports are received.
        // DeliveryFailureTracker tracks whether any error happened or not in any delivery report
        var flushSuccessful = FlushProducer(CancellationToken.None);
        if (!flushSuccessful)
        {
            _logger.LogWarning(
                "Kafka producer flush failed - short circuiting the execution - leaving messages in the Outbox Table");
            return false;
        }

        var earliestFailedId = deliveryFailureTracker.GetEarliestFailedMessageId();
        if (earliestFailedId.HasValue)
        {
            maxDeleteId = earliestFailedId.Value - 1;
            _logger.LogInformation(
                "Earliest delivery failure at message ID {FailedId}. Will delete messages up to ID {MaxId}",
                earliestFailedId.Value, maxDeleteId);
        }

        _memoryCache.Set(LastSentIdCacheKey, maxDeleteId);

        _ = Task.Run(async () =>
        {
            try
            {
                await outboxDbContext.OutboxMessages
                    .Where(om => om.Id <= maxDeleteId)
                    .ExecuteDeleteAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete outbox messages up to ID {MaxDeleteId}", maxDeleteId);
            }
            finally
            {
                await outboxDbContext.DisposeAsync();
            }
        }, CancellationToken.None);

        if (publishedMessagesCount > 0 && !earliestFailedId.HasValue)
        {
            var elapsedTime = Stopwatch.GetElapsedTime(startTimestamp);
            _logger.LogInformation("Published {Count} outbox messages in {ElapsedMMilliseconds}ms",
                publishedMessagesCount, elapsedTime.TotalMilliseconds);

            _outboxRelayMetrics.RecordProcessingDuration(elapsedTime, publishedMessagesCount);
        }

        if (publishedMessagesCount > 0 || failedMessagesByType.Count != 0)
        {
            foreach (var (messageType, count) in publishedMessagesByType)
            {
                _outboxRelayMetrics.RecordMessagesPublished(count, messageType);
            }

            foreach (var (messageType, count) in failedMessagesByType)
            {
                _outboxRelayMetrics.RecordMessagesFailed(count, messageType);
            }
        }

        return failedMessagesByType.Count == 0 && !earliestFailedId.HasValue;
    }

    private void PublishMessage(OutboxMessage outboxMessage, DeliveryFailureTracker deliveryFailureTracker)
    {
        var messageHeaders = outboxMessage.DeserializeHeaders();
        var kafkaMessage = new Message<string?, byte[]>
        {
            Key = outboxMessage.KafkaKey,
            Value = outboxMessage.AvroPayload,
            Headers = BuildKafkaHeaders(messageHeaders)
        };

        var topicName = _outboxRelayOptions.TypeTopicMappings.TryGetValue(
            string.Intern(outboxMessage.Type), out var topic)
            ? string.Intern(topic)
            : string.Intern(_outboxRelayOptions.DefaultTopicName);

        var producerActivity = KafkaProducerDiagnostics.StartProduceActivity(topicName, kafkaMessage, messageHeaders);

        // Don't use awaited ProduceAsync unless in http context,
        // see https://github.com/confluentinc/confluent-kafka-dotnet/wiki/Producer
        // and https://docs.confluent.io/kafka-clients/dotnet/current/overview.html
        // The Produce() method is also asynchronous, in that it never blocks.
        // Message delivery information is made available out-of-band via the (optional) delivery report handler on a background thread
        // Benchmark here https://concurrentflows.com/kafka-producer-sync-vs-async had 6000x more throughput with sync
        // Produce over ProduceAsync
        _kafkaProducer.Produce(
            topicName,
            kafkaMessage,
            deliveryReport =>
            {
                // deliveryReport runs on background thread -> thread-safe delivery tracking
                try
                {
                    if (deliveryReport.Error.Code != ErrorCode.NoError)
                    {
                        deliveryFailureTracker.RecordFailure(outboxMessage.Id);

                        _logger.LogError(
                            "Failed to deliver outbox message {MessageId}: {ErrorCode} {ErrorReason}",
                            outboxMessage.Id, deliveryReport.Error.Code, deliveryReport.Error.Reason);

                        producerActivity?.SetStatus(ActivityStatusCode.Error, deliveryReport.Error.Reason);
                        producerActivity?.SetTag(OutboxDiagnosticNames.ErrorTag.Type,
                            deliveryReport.Error.Code.ToString());
                    }
                    else if (deliveryReport.Status != PersistenceStatus.Persisted)
                    {
                        deliveryFailureTracker.RecordFailure(outboxMessage.Id);

                        _logger.LogError(
                            "Message {MessageId} with {KafkaKey} was not persisted",
                            outboxMessage.Id, outboxMessage.KafkaKey);

                        producerActivity?.SetStatus(ActivityStatusCode.Error, "Message not persisted to Kafka");
                    }
                    else
                    {
                        producerActivity?.SetTag(OutboxDiagnosticNames.Kafka.MessageOffset, deliveryReport.Offset);
                    }
                }
                finally
                {
                    producerActivity?.Dispose();
                }
            });
    }

    private static Headers BuildKafkaHeaders(Dictionary<string, string>? messageHeaders)
    {
        var kafkaHeaders = new Headers();

        if (messageHeaders == null)
        {
            return kafkaHeaders;
        }

        foreach (var (key, value) in messageHeaders)
        {
            if (!string.IsNullOrEmpty(value))
            {
                kafkaHeaders.Add(key, Encoding.UTF8.GetBytes(value));
            }
        }

        return kafkaHeaders;
    }

    /// <summary>
    /// Flushes pending messages to Kafka - wait until all outstanding produce requests and delivery report callbacks are completed.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if flush completed successfully, false if canceled or error occurred.</returns>
    public bool FlushProducer(CancellationToken ct = default)
    {
        var flushTimeout = TimeSpan.FromMilliseconds(_outboxRelayOptions.FlushTimeoutMs);
        _logger.LogDebug("Flushing Kafka producer with timeout {TimeoutMs}ms", flushTimeout.TotalMilliseconds);

        try
        {
            using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            flushCts.CancelAfter(flushTimeout);
            _kafkaProducer.Flush(flushCts.Token);

            _logger.LogDebug("Kafka producer flush completed successfully");
            return true;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex,
                "Kafka producer flush was canceled - likely due to timeout or external cancellation");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during Kafka producer flush");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Disposing Kafka producer");

            var flushSuccessful = FlushProducer();
            if (!flushSuccessful)
            {
                _logger.LogWarning("Kafka producer flush failed");
            }

            _kafkaProducer.Dispose();

            _logger.LogInformation("Kafka producer disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disposal of Kafka producer");
        }
        finally
        {
            _disposed = true;
        }
    }
}
