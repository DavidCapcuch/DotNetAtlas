using FluentResults;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Ordering.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Common scaffolding for Ordering's saga-command Kafka consumers. Each
/// concrete handler implements <see cref="IMessageHandler{T}"/> over its
/// Avro command type and calls <see cref="ExecuteAsync"/> with a delegate
/// that dispatches the translated application command.
/// </summary>
/// <remarks>
/// <para>Responsibilities:</para>
/// <list type="bullet">
/// <item>Wraps handler execution in <see cref="ITransactionalOutbox{TContext}"/>'s
/// <c>EnsureTransactionAsync</c> so domain-event outbox writes are atomic
/// with aggregate mutation.</item>
/// <item>Pushes <c>OrderId</c> into the logger scope so every log line
/// the handler emits — and every nested Application-layer log — is
/// queryable by it in Seq. The <c>CorrelationId</c> Serilog property
/// is pushed by <c>ConsumerCorrelationIdMiddleware</c> at the consumer
/// pipeline edge from the Kafka header (ADR-0008 runbook-first
/// operability); duplicating it here would risk drift between the
/// header value and the Avro-payload value.</item>
/// <item>On <see cref="Result.IsFailed"/>, throws a
/// <see cref="SagaCommandDispatchException"/> so the KafkaFlow retry +
/// DLT middleware can handle transient vs poison-pill classification.</item>
/// </list>
/// </remarks>
internal abstract class SagaCommandHandlerBase<TAvroCommand>
    where TAvroCommand : class
{
    private readonly ITransactionalOutbox<IOrderingDbContext> _transactionalOutbox;
    private readonly ILogger _logger;

    protected SagaCommandHandlerBase(
        ITransactionalOutbox<IOrderingDbContext> transactionalOutbox,
        ILogger logger)
    {
        _transactionalOutbox = transactionalOutbox;
        _logger = logger;
    }

    /// <param name="context">Inbound Kafka message context.</param>
    /// <param name="correlationId">Saga correlation id from the Avro payload.</param>
    /// <param name="orderId">Target order id, or <c>null</c> for <c>CreateOrderCommand</c>.</param>
    /// <param name="dispatchAsync">
    /// Dispatches the translated application command and returns the
    /// FluentResults outcome (short-form <see cref="Result"/> — callers with
    /// a <see cref="Result{T}"/> collapse via <c>.ToResult()</c>).
    /// </param>
    protected async Task ExecuteAsync(
        IMessageContext context,
        Guid correlationId,
        Guid? orderId,
        Func<CancellationToken, Task<Result>> dispatchAsync)
    {
        var origin = context.ExtractOrigin();
        var cancellationToken = context.ConsumerContext.WorkerStopped;

        // CorrelationId is already in the Serilog LogContext via
        // ConsumerCorrelationIdMiddleware (Kafka header is the source of truth);
        // we only push OrderId here so per-order log queries work.
        using var orderScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OrderId"] = orderId,
        });

        _logger.LogInformation(
            "Handling {CommandType} from origin {Origin} (CorrelationId={CorrelationId}, OrderId={OrderId})",
            typeof(TAvroCommand).Name, origin ?? "unknown", correlationId, orderId);

        await _transactionalOutbox.Database.EnsureTransactionAsync(async () =>
        {
            // Inner application handler owns SaveChangesAsync — its single commit
            // covers the aggregate mutation plus the domain-event-driven outbox
            // rows inserted in the same change-tracker. EnsureTransactionAsync
            // wraps both calls in the same DbContext transaction, so reliable
            // messaging stays atomic without a second base-level save.
            var result = await dispatchAsync(cancellationToken);

            if (result.IsFailed)
            {
                var errorSummary = string.Join("; ", result.Errors.Select(e => e.Message));
                _logger.LogWarning(
                    "{CommandType} dispatch failed (CorrelationId={CorrelationId}, OrderId={OrderId}): {Errors}",
                    typeof(TAvroCommand).Name, correlationId, orderId, errorSummary);

                throw new SagaCommandDispatchException(
                    $"Dispatch of {typeof(TAvroCommand).Name} failed: {errorSummary}");
            }

            _logger.LogInformation(
                "{CommandType} handled successfully (CorrelationId={CorrelationId}, OrderId={OrderId})",
                typeof(TAvroCommand).Name, correlationId, orderId);
        }, cancellationToken);
    }
}

/// <summary>
/// Thrown when a saga-command dispatch returns <see cref="Result.IsFailed"/>.
/// Classified as poison by the Kafka retry middleware (non-transient) so the
/// message flows to the command topic's DLT for operator inspection.
/// </summary>
public sealed class SagaCommandDispatchException : Exception
{
    public SagaCommandDispatchException(string message)
        : base(message)
    {
    }

    public SagaCommandDispatchException()
    {
    }

    public SagaCommandDispatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
