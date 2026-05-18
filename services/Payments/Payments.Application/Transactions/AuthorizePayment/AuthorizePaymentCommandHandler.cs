using FluentResults;
using Microsoft.Extensions.Logging;
using Payments.Application.Abstractions;
using Payments.Application.Common.Data;
using Payments.Domain.Errors;
using Payments.Domain.Transactions;
using Payments.Domain.Transactions.ValueObjects;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Application.Transactions.AuthorizePayment;

/// <summary>
/// Handles <see cref="AuthorizePaymentCommand"/> — the saga's entry point into the Payments
/// BC. Loads (or creates) the aggregate, calls the gateway, transitions the aggregate, and
/// flushes outbox rows + aggregate state in a single transaction.
/// </summary>
/// <remarks>
/// <para>
/// Failure semantics:
/// <list type="bullet">
///   <item><description>Gateway <b>decline</b> (business-expected, e.g. <c>insufficient_funds</c>):
///     aggregate transitions to <c>Failed</c> and the handler returns <c>Result.Ok()</c>. The
///     saga learns of the decline via the emitted <c>PaymentAuthorizationFailedEvent</c> on
///     the outbox.</description></item>
///   <item><description>Gateway <b>infrastructure error</b> (timeout, unreachable): handler
///     returns <c>Result.Fail(PaymentsErrors.GatewayUnavailable())</c>. The saga / outbox
///     relay retries per ADR-0010.</description></item>
///   <item><description>Aggregate already past <c>Requested</c>: idempotent no-op. Saga retries
///     are deduplicated by the M5 inbox; this short-circuit is defense-in-depth.</description></item>
/// </list>
/// </para>
/// </remarks>
internal sealed class AuthorizePaymentCommandHandler : ICommandHandler<AuthorizePaymentCommand, Guid>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentGateway _gateway;
    private readonly ITransactionalOutbox<IPaymentsDbContext> _outbox;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthorizePaymentCommandHandler> _logger;

    public AuthorizePaymentCommandHandler(
        IPaymentRepository repository,
        IPaymentGateway gateway,
        ITransactionalOutbox<IPaymentsDbContext> outbox,
        IDomainEventDispatcher dispatcher,
        TimeProvider timeProvider,
        ILogger<AuthorizePaymentCommandHandler> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _outbox = outbox;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(AuthorizePaymentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _repository.GetByIdAsync(command.PaymentId, ct);

        PaymentTransaction tx;
        if (existing is null)
        {
            var moneyResult = Money.Create(command.Amount, command.Currency);
            if (moneyResult.IsFailed)
            {
                return moneyResult.ToResult<Guid>();
            }

            var createResult = PaymentTransaction.Create(
                command.PaymentId,
                command.CorrelationId,
                command.BuyerId,
                command.OrderId,
                moneyResult.Value,
                command.PaymentMethodId,
                _timeProvider.GetUtcNow());
            if (createResult.IsFailed)
            {
                return createResult.ToResult<Guid>();
            }

            tx = createResult.Value;
            _repository.Add(tx);
        }
        else
        {
            tx = existing;
            if (tx.Status != PaymentStatus.Requested)
            {
                _logger.LogInformation(
                    "AuthorizePaymentCommand received for {PaymentId} already in '{Status}'; idempotent no-op.",
                    tx.Id,
                    tx.Status.Name);
                return Result.Ok(tx.Id);
            }
        }

        var gatewayResult = await _gateway.AuthorizeAsync(tx, command.IdempotencyKey, ct);
        var utcNow = _timeProvider.GetUtcNow();

        if (gatewayResult.IsSuccess)
        {
            var authorizeResult = tx.Authorize(
                gatewayResult.Value.GatewayTransactionId,
                gatewayResult.Value.ResponseCode,
                gatewayResult.Value.ExpiresAtUtc,
                utcNow);
            if (authorizeResult.IsFailed)
            {
                return authorizeResult.ToResult<Guid>();
            }
        }
        else
        {
            var declined = gatewayResult.Errors.OfType<GatewayDeclinedError>().FirstOrDefault();
            if (declined is null)
            {
                _logger.LogWarning(
                    "Gateway authorize call for {PaymentId} returned an infrastructure error; saga must retry.",
                    tx.Id);
                return Result.Fail<Guid>(PaymentsErrors.GatewayUnavailable());
            }

            var reason = GatewayResponseClassifier.Classify(declined.GatewayCode);
            var failureInfo = new FailureInfo(reason, declined.GatewayCode, utcNow);
            var failedResult = tx.MarkAuthorizationFailed(failureInfo, utcNow);
            if (failedResult.IsFailed)
            {
                return failedResult.ToResult<Guid>();
            }
        }

        foreach (var domainEvent in tx.PopDomainEvents())
        {
            await _dispatcher.DispatchAsync(domainEvent, ct);
        }

        await _outbox.SaveChangesAsync(ct);

        return Result.Ok(tx.Id);
    }
}
