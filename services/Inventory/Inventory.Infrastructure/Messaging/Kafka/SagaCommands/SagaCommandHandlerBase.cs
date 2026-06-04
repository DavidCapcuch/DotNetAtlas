using FluentResults;
using Inventory.Application.Common.Data;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.SharedKernel.Errors;

namespace Inventory.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Common scaffolding for Inventory's saga-command Kafka consumers. Each
/// concrete handler implements <see cref="IMessageHandler{T}"/> over its
/// Avro command type and calls <see cref="ExecuteAsync"/> with a delegate
/// that dispatches the translated application command.
/// </summary>
/// <remarks>
/// <para>Responsibilities:</para>
/// <list type="bullet">
/// <item>Wraps handler execution in
/// <see cref="ITransactionalOutbox{TContext}"/>'s
/// <c>EnsureTransactionAsync</c> so domain-event outbox writes commit
/// atomically with the event-store append + projection updates. The
/// inbox middleware also opens its own transaction (covering the
/// <c>InboxMessage</c> insert + the typed handler call); the
/// <c>EnsureTransactionAsync</c> here joins the ambient inbox tx as a
/// no-op when one is open
/// (see <c>platform/Platform.ReliableMessaging.Outbox.EFCore/Common/DatabaseFacadeExtensions.cs:23-27</c>),
/// keeping a single tx end-to-end.</item>
/// <item>Pushes a caller-supplied set of log-context keys into the Serilog
/// <c>BeginScope</c> so every log line the handler emits — and every
/// nested Application-layer log — is queryable by those ids in Seq
/// (runbook-first operability). Inventory's saga commands have
/// variable id shape (<c>ReserveStock</c> carries <c>OrderId</c>;
/// <c>Confirm</c>/<c>Release</c> carry only
/// <c>ProductId</c>+<c>ReservationId</c>), so callers pass exactly the
/// ids they care about.</item>
/// <item>On <see cref="Result.IsFailed"/> from the dispatch delegate,
/// throws a <see cref="SagaCommandDispatchException"/> so the KafkaFlow
/// retry + DLT middleware classifies the message as poison (non-transient)
/// and routes it to the command topic's DLT for operator inspection.</item>
/// </list>
/// </remarks>
internal abstract class SagaCommandHandlerBase<TAvroCommand>
    where TAvroCommand : class
{
    /// <summary>
    /// Error codes the application layer returns as <c>Result.Fail</c> AFTER
    /// staging the saga-visible failure event in the outbox (e.g. the
    /// <c>StockReservationFailedEvent</c> emitted by
    /// <c>ReserveStockCommandHandler</c> on
    /// <see cref="Inventory.Domain.StockItems.Errors.InsufficientStockError"/>).
    /// For these, throwing here would roll back the inbox tx — taking the
    /// staged outbox row with it — so the saga would never see the failure
    /// event. The wrapper therefore commits the tx and returns silently.
    /// </summary>
    /// <remarks>
    /// <para>Addition criteria:</para>
    /// <list type="bullet">
    /// <item><b>MUST add:</b> error codes whose application-layer
    /// <c>Result.Fail</c> path also stages an outbox-backed saga response
    /// before returning. Without the entry, the wrapper's throw rolls the
    /// staged response back.</item>
    /// <item><b>MUST NOT add:</b> error codes whose <c>Result.Fail</c> path
    /// does NOT stage an outbox response — e.g. early-exit validation
    /// failures (<c>ReservationId.Empty</c>) or
    /// <see cref="Inventory.Domain.StockItems.Errors.ConcurrencyError"/>
    /// returned after exhausted retry. For those, DLT-routing is the right
    /// behavior — the saga has no response to consume so operator triage
    /// is the next step.</item>
    /// <item><b>Bug-class conditions</b> (re-init, unknown reservation,
    /// non-Active confirm) are <i>thrown</i> by the aggregate as
    /// <c>DataIntegrityException</c> — they never reach this filter at all.
    /// They DLT-route via the unhandled-exception path.</item>
    /// </list>
    /// </remarks>
    private static readonly HashSet<string> BusinessExpectedErrorCodes = new(StringComparer.Ordinal)
    {
        "Inventory.InsufficientStock",
    };

    private readonly ITransactionalOutbox<IInventoryDbContext> _transactionalOutbox;
    private readonly ILogger _logger;

    protected SagaCommandHandlerBase(
        ITransactionalOutbox<IInventoryDbContext> transactionalOutbox,
        ILogger logger)
    {
        _transactionalOutbox = transactionalOutbox;
        _logger = logger;
    }

    /// <param name="context">Inbound Kafka message context.</param>
    /// <param name="logContext">
    /// Caller-chosen log-context keys (e.g. <c>{"OrderId", orderId,
    /// "ReservationId", reservationId}</c>). Pushed into the Serilog
    /// <c>BeginScope</c> for the duration of dispatch so nested logs
    /// inherit them.
    /// </param>
    /// <param name="dispatchAsync">
    /// Dispatches the translated application command and returns the
    /// FluentResults outcome.
    /// </param>
    protected async Task ExecuteAsync(
        IMessageContext context,
        Dictionary<string, object?> logContext,
        Func<CancellationToken, Task<Result>> dispatchAsync)
    {
        var origin = context.ExtractOrigin();
        var cancellationToken = context.ConsumerContext.WorkerStopped;

        using var correlationScope = _logger.BeginScope(logContext);

        _logger.LogInformation(
            "Handling {CommandType} from origin {Origin}",
            typeof(TAvroCommand).Name, origin ?? "unknown");

        await _transactionalOutbox.Database.EnsureTransactionAsync(async () =>
        {
            var result = await dispatchAsync(cancellationToken);

            if (result.IsFailed)
            {
                var errorSummary = string.Join("; ", result.Errors.Select(e => e.Message));

                // Business-expected failure (e.g. InsufficientStock): the
                // application handler has already staged the saga-visible
                // outbox event. Commit and exit without throwing — the
                // failure is the saga's signal, not ours, and throwing here
                // would roll back the staged outbox row (and the inbox row).
                if (IsBusinessExpectedFailure(result.Errors))
                {
                    _logger.LogInformation(
                        "{CommandType} returned business-expected failure; committing staged saga response: {Errors}",
                        typeof(TAvroCommand).Name, errorSummary);

                    await _transactionalOutbox.SaveChangesAsync(cancellationToken);
                    return;
                }

                _logger.LogWarning(
                    "{CommandType} dispatch failed: {Errors}",
                    typeof(TAvroCommand).Name, errorSummary);

                throw new SagaCommandDispatchException(
                    $"Dispatch of {typeof(TAvroCommand).Name} failed: {errorSummary}");
            }

            await _transactionalOutbox.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "{CommandType} handled successfully",
                typeof(TAvroCommand).Name);
        }, cancellationToken);
    }

    private static bool IsBusinessExpectedFailure(IEnumerable<FluentResults.IError> errors)
    {
        foreach (var error in errors)
        {
            if (error is DomainError domainError
                && BusinessExpectedErrorCodes.Contains(domainError.ErrorCode))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Thrown when a saga-command dispatch returns
/// <see cref="Result.IsFailed"/>. Classified as poison by the Kafka retry
/// middleware (non-transient) so the message flows to the command topic's
/// DLT for operator inspection.
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
