using FluentResults;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Payments.Application.Common.Data;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Payments.Infrastructure.Messaging.Kafka.PaymentCommands;

/// <summary>
/// Common scaffolding for Payments' saga-command Kafka consumers. Each concrete handler
/// implements <see cref="IMessageHandler{T}"/> over its Avro command type and calls
/// <see cref="ExecuteAsync"/> with a delegate that dispatches the translated application
/// command.
/// </summary>
/// <remarks>
/// <para>Responsibilities:</para>
/// <list type="bullet">
/// <item>Wraps handler execution in <see cref="ITransactionalOutbox{TContext}"/>'s
/// <c>EnsureTransactionAsync</c> so domain-event outbox writes are atomic with aggregate
/// mutation.</item>
/// <item>Pushes a caller-supplied set of business ids (<c>OrderId</c> and/or <c>PaymentId</c>)
/// into the Serilog <c>BeginScope</c> so every log line the handler emits — and every nested
/// Application-layer log — is queryable by those ids in Seq. Cross-process correlation is carried
/// by the ambient <c>traceId</c> (W3C / OpenTelemetry).</item>
/// <item>On <see cref="Result.IsFailed"/>, throws a <see cref="SagaCommandDispatchException"/>
/// so the KafkaFlow retry + DLT middleware can handle transient vs poison-pill
/// classification.</item>
/// </list>
/// </remarks>
internal abstract class SagaCommandHandlerBase<TAvroCommand>
    where TAvroCommand : class
{
    private readonly ITransactionalOutbox<IPaymentsDbContext> _transactionalOutbox;
    private readonly ILogger _logger;

    protected SagaCommandHandlerBase(
        ITransactionalOutbox<IPaymentsDbContext> transactionalOutbox,
        ILogger logger)
    {
        _transactionalOutbox = transactionalOutbox;
        _logger = logger;
    }

    /// <param name="context">Inbound Kafka message context.</param>
    /// <param name="logScope">
    /// Business ids to push into the Serilog <c>BeginScope</c> for the duration of dispatch (e.g.
    /// <c>{"OrderId", orderId}</c> for Capture/Void, <c>{"OrderId", …, "PaymentId", …}</c> for
    /// Authorize) so nested logs inherit them. The ambient <c>traceId</c> already rides on every
    /// log line via the OpenTelemetry enrichment.
    /// </param>
    /// <param name="dispatchAsync">
    /// Dispatches the translated application command and returns the FluentResults outcome
    /// (short-form <see cref="Result"/> — callers with a <see cref="Result{T}"/> collapse via
    /// <c>.ToResult()</c>).
    /// </param>
    protected async Task ExecuteAsync(
        IMessageContext context,
        Dictionary<string, object?> logScope,
        Func<CancellationToken, Task<Result>> dispatchAsync)
    {
        var origin = context.ExtractOrigin();
        var cancellationToken = context.ConsumerContext.WorkerStopped;

        using var scope = _logger.BeginScope(logScope);

        _logger.LogInformation(
            "Handling {CommandType} from origin {Origin}",
            typeof(TAvroCommand).Name, origin ?? "unknown");

        await _transactionalOutbox.Database.EnsureTransactionAsync(async () =>
        {
            var result = await dispatchAsync(cancellationToken);

            if (result.IsFailed)
            {
                var errorSummary = string.Join("; ", result.Errors.Select(e => e.Message));
                _logger.LogWarning(
                    "{CommandType} dispatch failed: {Errors}",
                    typeof(TAvroCommand).Name, errorSummary);

                throw new SagaCommandDispatchException(
                    $"Dispatch of {typeof(TAvroCommand).Name} failed: {errorSummary}");
            }

            await _transactionalOutbox.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "{CommandType} handled successfully", typeof(TAvroCommand).Name);
        }, cancellationToken);
    }
}

/// <summary>
/// Thrown when a saga-command dispatch returns <see cref="Result.IsFailed"/>. Classified as poison
/// by the Kafka retry middleware (non-transient) so the message flows to the command topic's DLT
/// for operator inspection.
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
