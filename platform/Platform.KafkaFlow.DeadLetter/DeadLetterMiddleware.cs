using System.Text;
using KafkaFlow;
using Microsoft.Extensions.Logging;

namespace Platform.KafkaFlow.DeadLetter;

/// <summary>
/// Middleware that catches unhandled exceptions and sends messages to a Dead Letter Topic.
/// Messages sent to DLT are not retried and offset is committed.
/// </summary>
/// <remarks>
/// <see cref="OperationCanceledException"/> is rethrown to allow graceful shutdown - the message remains
/// uncommitted for reprocessing after restart.
/// </remarks>
internal sealed class DeadLetterMiddleware : IMessageMiddleware
{
    private readonly IMessageProducer<DeadLetterMiddleware> _dltProducer;
    private readonly string _topicSuffix;
    private readonly ILogger<DeadLetterMiddleware> _logger;

    public DeadLetterMiddleware(
        IMessageProducer<DeadLetterMiddleware> dltProducer,
        string topicSuffix,
        ILogger<DeadLetterMiddleware> logger)
    {
        _dltProducer = dltProducer;
        _topicSuffix = topicSuffix;
        _logger = logger;
    }

    public async Task Invoke(IMessageContext context, MiddlewareDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown signal - let the message remain uncommitted for reprocessing after restart
            throw;
        }
        catch (Exception ex)
        {
            // Don't rethrow - message will be committed
            await SendToDltAsync(context, ex);
        }
    }

    private async Task SendToDltAsync(IMessageContext context, Exception exception)
    {
        var dltHeaders = CreateDltHeaders(context, exception);

        var originalTopic = context.ConsumerContext.Topic;
        var dltTopic = $"{originalTopic}{_topicSuffix}";

        var key = context.Message.Key is byte[] keyBytes
            ? Encoding.UTF8.GetString(keyBytes)
            : context.Message.Key;

        // One record per outcome, and never one that claims a routing that did not happen. The
        // broker does not auto-create topics, so a DLT absent from the kafka-create-topic block
        // makes ProduceAsync throw; KafkaFlow's worker commits the offset regardless, so the
        // message is dropped. An operator reading "routed to DLT x" and finding x empty has been
        // told the opposite of what occurred.
        try
        {
            await _dltProducer.ProduceAsync(dltTopic, context.Message.Key, context.Message.Value, dltHeaders);
        }
        catch (Exception produceFailure)
        {
            _logger.LogError(
                new AggregateException(
                    $"Poison message with key {key} could not be routed to dead-letter topic "
                    + $"{dltTopic}. The offset commits regardless, so this message is dropped.",
                    exception,
                    produceFailure),
                "Dead-letter routing FAILED for {Key} on {DltTopic}; message dropped",
                key,
                dltTopic);

            throw;
        }

        _logger.LogError(exception,
            "Message with key {Key} routed to DLT {DltTopic}",
            key,
            dltTopic);
    }

    private static MessageHeaders CreateDltHeaders(IMessageContext context, Exception exception)
    {
        var dltHeaders = new MessageHeaders();

        foreach (var header in context.Headers)
        {
            dltHeaders.Add(header.Key, header.Value);
        }

        dltHeaders.Add(DltHeaders.OriginalTopic, Encoding.UTF8.GetBytes(context.ConsumerContext.Topic));
        dltHeaders.Add(DltHeaders.OriginalPartition,
            Encoding.UTF8.GetBytes(context.ConsumerContext.Partition.ToString()));
        dltHeaders.Add(DltHeaders.OriginalOffset, Encoding.UTF8.GetBytes(context.ConsumerContext.Offset.ToString()));

        dltHeaders.Add(DltHeaders.ExceptionType, Encoding.UTF8.GetBytes(exception.GetType().FullName ?? "Unknown"));
        dltHeaders.Add(DltHeaders.ExceptionMessage, Encoding.UTF8.GetBytes(exception.Message));
        dltHeaders.Add(DltHeaders.ExceptionStackTrace, Encoding.UTF8.GetBytes(exception.ToString()));

        return dltHeaders;
    }
}
